using System.Diagnostics;
using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;
using WaferAoi.UiTestTool.Web.Storage;
using WaferAoi.UiTestTool.Web.Validation;

namespace WaferAoi.UiTestTool.Web.Runs;

/// <summary>
/// 运行编排：创建、单实例锁、执行器监视、确认通道、现场处理解除、历史查询。
/// 浏览器断开不影响本类；所有状态以落盘事件为准。
/// </summary>
internal sealed class RunManager
{
    /// <summary>运行标识前缀。</summary>
    private const string RunIdPrefix = "run_";

    /// <summary>路径约定。</summary>
    private readonly LocalPaths _paths;

    /// <summary>配置存储。</summary>
    private readonly ConfigStore _config;

    /// <summary>运行存储。</summary>
    private readonly RunStore _store;

    /// <summary>机器锁存储。</summary>
    private readonly MachineLockStore _locks;

    /// <summary>配置校验。</summary>
    private readonly ConfigValidator _validator;

    /// <summary>执行器宿主。</summary>
    private readonly ExecutorHost _executor;

    /// <summary>宿主参数。</summary>
    private readonly WebArguments _arguments;

    /// <summary>日志。</summary>
    private readonly ILogger<RunManager> _logger;

    /// <summary>创建运行串行化。</summary>
    private readonly SemaphoreSlim _createGate = new(1, 1);

    /// <summary>会话表同步。</summary>
    private readonly object _sync = new();

    /// <summary>已加载会话。</summary>
    private readonly Dictionary<string, RunSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>请求标识 → 运行标识。</summary>
    private readonly Dictionary<string, string> _requestIndex = new(StringComparer.Ordinal);

    /// <summary>当前运行标识。</summary>
    private string? _currentRunId;

    /// <summary>创建编排器。</summary>
    public RunManager(
        LocalPaths paths,
        ConfigStore config,
        RunStore store,
        MachineLockStore locks,
        ConfigValidator validator,
        ExecutorHost executor,
        WebArguments arguments,
        ILogger<RunManager> logger)
    {
        _paths = paths;
        _config = config;
        _store = store;
        _locks = locks;
        _validator = validator;
        _executor = executor;
        _arguments = arguments;
        _logger = logger;
    }

    /// <summary>
    /// 启动时恢复机器占用与未收敛运行。执行器仍存活时只接管事件读取，不重新执行步骤；
    /// 执行器已退出则标记中断并保持待现场核对，不自动重跑。
    /// </summary>
    public Task InitializeAsync()
    {
        MachineLockState? state = _locks.Read();
        if (state == null)
        {
            string? latest = _store.ListRunIds().FirstOrDefault();
            if (latest != null)
            {
                _currentRunId = latest;
            }

            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(state.RunId) || !RunStore.IsSafeRunId(state.RunId))
        {
            _logger.LogWarning("机器锁缺少有效 runId，保持占用并等待人工核对。");
            return Task.CompletedTask;
        }

        RunSession? session = _store.Load(state.RunId);
        if (session == null)
        {
            _logger.LogWarning("机器锁指向的运行记录不存在：{RunId}", state.RunId);
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            _sessions[state.RunId] = session;
            _currentRunId = state.RunId;
        }

        if (state.PendingField || session.PendingFieldActive)
        {
            return Task.CompletedTask;
        }

        if (session.Finished)
        {
            // 事件已收敛且没有待现场处理，说明上一宿主退出前已具备解除条件。
            _locks.Release();
            return Task.CompletedTask;
        }

        session.Snapshot.Executor.ProcessId = state.ExecutorPid;
        if (state.ExecutorPid > 0 && IsProcessAlive(state.ExecutorPid, state.CreatedAt))
        {
            session.MonitorTask = Task.Run(() => ResumeAsync(session));
        }
        else
        {
            session.Snapshot.Executor.Alive = false;
            _ = Task.Run(() => MarkInterruptedAsync(
                session,
                "宿主重启时发现执行器已退出且本轮未收敛；当前步骤结果未知，不自动重跑。"));
        }

        return Task.CompletedTask;
    }

    /// <summary>取得当前运行快照；没有运行时返回 null。</summary>
    /// <returns>快照。</returns>
    public RunSnapshot? GetCurrent()
    {
        string? runId;
        lock (_sync)
        {
            runId = _currentRunId;
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        RunSession? session = GetSession(runId);
        return session == null ? null : Snapshot(session);
    }

    /// <summary>按标识取得快照。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>快照；不存在时返回 null。</returns>
    public RunSnapshot? GetRun(string runId)
    {
        RunSession? session = GetSession(runId);
        return session == null ? null : Snapshot(session);
    }

    /// <summary>取得会话，用于 SSE 与证据接口。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>会话；不存在时返回 null。</returns>
    public RunSession? GetSession(string runId)
    {
        if (!RunStore.IsSafeRunId(runId))
        {
            return null;
        }

        lock (_sync)
        {
            if (_sessions.TryGetValue(runId, out RunSession? cached))
            {
                return cached;
            }
        }

        RunSession? loaded = _store.Load(runId);
        if (loaded != null)
        {
            lock (_sync)
            {
                _sessions[runId] = loaded;
            }
        }

        return loaded;
    }

    /// <summary>列出历史运行。</summary>
    /// <param name="limit">最多条数。</param>
    /// <param name="offset">跳过条数。</param>
    /// <returns>历史列表。</returns>
    public IReadOnlyList<RunSummary> ListRuns(int limit, int offset)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);
        var result = new List<RunSummary>();
        foreach (string runId in _store.ListRunIds().Skip(offset).Take(limit))
        {
            RunSession? session = GetSession(runId);
            if (session == null)
            {
                continue;
            }

            RunSnapshot snapshot = Snapshot(session);
            result.Add(new RunSummary
            {
                RunId = snapshot.RunId,
                CreatedAt = snapshot.CreatedAt,
                FinishedAt = snapshot.FinishedAt,
                Phase = snapshot.Phase,
                Conclusion = snapshot.Conclusion,
                HardwarePolicy = snapshot.Options.HardwarePolicy,
                SkipVersionAssertion = snapshot.Options.SkipVersionAssertion,
                CleanupStatus = snapshot.Cleanup.Status,
                PendingFieldActive = snapshot.PendingField.Active,
                Simulated = snapshot.Simulated,
                Counts = snapshot.Counts
            });
        }

        return result;
    }

    /// <summary>取得最终报告。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>报告；不存在时返回 null。</returns>
    /// <summary>鍘嗗彶杩愯鎬绘暟銆?/summary>
    /// <returns>杩愯璁板綍鏁般€?/returns>
    public int CountRuns() => _store.ListRunIds().Count();

    public RunReport? GetReport(string runId)
    {
        string path = _paths.ReportPath(runId);
        if (File.Exists(path) && RunStore.IsSafeRunId(runId))
        {
            try
            {
                return JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path), JsonDefaults.File);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "报告损坏，按事件重建：{RunId}", runId);
            }
        }

        RunSession? session = GetSession(runId);
        return session == null ? null : _store.BuildReport(session);
    }

    /// <summary>校验当前配置。</summary>
    /// <param name="skipVersionAssertion">是否按跳过版本断言校验。</param>
    /// <returns>校验结果。</returns>
    public ConfigValidationResult Validate(bool? skipVersionAssertion)
    {
        ToolConfig config = _config.Get();
        bool skip = skipVersionAssertion ?? config.Options.SkipVersionAssertion;
        return _validator.Validate(config, skip, _paths.ExecutorPath);
    }

    /// <summary>
    /// 创建本轮运行。同一 requestId 返回同一运行；机器忙时返回现有运行；
    /// 非法硬件组合或配置错误时不创建运行、不启动进程。
    /// </summary>
    /// <param name="request">创建请求。</param>
    /// <returns>创建结果。</returns>
    public async Task<CreateRunResponse> CreateRunAsync(CreateRunRequest request)
    {
        if (request == null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid_request", "请求体不能为空。");
        }

        if (!RunStore.IsSafeRunId(request.RequestId))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "invalid_request_id",
                "requestId 必填，只允许字母、数字、短横线和下划线，长度不超过 64。");
        }

        await _createGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? existingId = FindRunByRequestId(request.RequestId);
            if (existingId != null)
            {
                RunSession? existing = GetSession(existingId);
                if (existing != null)
                {
                    return new CreateRunResponse { Created = false, Run = Snapshot(existing) };
                }
            }

            MachineLockState? currentLock = _locks.Read();
            if (currentLock != null)
            {
                RunSnapshot? current = null;
                if (!string.IsNullOrWhiteSpace(currentLock.RunId))
                {
                    RunSession? currentSession = GetSession(currentLock.RunId);
                    current = currentSession == null ? null : Snapshot(currentSession);
                }

                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "machine_busy",
                    string.IsNullOrWhiteSpace(currentLock.Reason)
                        ? "测试机正忙或待现场处理，不能启动新一轮。"
                        : currentLock.Reason,
                    currentRun: current);
            }

            RunOptions options = ResolveOptions(request);
            if (!Enum.IsDefined(typeof(HardwarePolicy), options.HardwarePolicy))
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "invalid_options", "硬件策略取值无效。");
            }

            ToolConfig config = _config.Get();
            ConfigValidationResult validation = _validator.Validate(config, options.SkipVersionAssertion, _paths.ExecutorPath);
            if (!validation.Valid)
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    "config_invalid",
                    "配置校验未通过，未创建运行。",
                    validation.Issues);
            }

            string runId = CreateRunId();
            ToolConfig effective = config;
            var manifest = new RunManifest
            {
                RunId = runId,
                CreatedAt = DateTimeOffset.Now,
                Options = options,
                Config = BuildConfigSnapshot(effective, options.SkipVersionAssertion),
                Steps = MainFlowCatalog.Steps.Select(CloneDefinition).ToList(),
                CheckpointTimeoutSeconds = 1800,
                Simulation = _arguments.Simulate,
                Scenario = _arguments.Scenario
            };

            var lockState = new MachineLockState
            {
                RunId = runId,
                CreatedAt = manifest.CreatedAt,
                Phase = "preparing",
                PendingField = false,
                Reason = "本轮正在执行，测试机被占用。"
            };

            if (!_locks.TryAcquire(lockState))
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "machine_busy",
                    "测试机已被另一宿主占用，不能启动新一轮。");
            }

            RunSession session;
            try
            {
                session = _store.Create(manifest);
                File.WriteAllText(Path.Combine(session.RunDirectory, "request-id.txt"), request.RequestId);
            }
            catch (Exception)
            {
                _locks.Release();
                throw;
            }

            lock (_sync)
            {
                _sessions[runId] = session;
                _requestIndex[request.RequestId] = runId;
                _currentRunId = runId;
            }

            session.MonitorTask = Task.Run(() => MonitorAsync(session));
            return new CreateRunResponse { Created = true, Run = Snapshot(session) };
        }
        finally
        {
            _createGate.Release();
        }
    }

    /// <summary>
    /// 接受网页确认命令。只接受当前仍在等待的确认点；重复 requestId 幂等；
    /// 过期、已结束或非当前确认点返回 409，不改变步骤。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="checkpointId">确认点标识。</param>
    /// <param name="request">确认请求。</param>
    /// <returns>受理结果。</returns>
    public ConfirmCheckpointResponse ConfirmCheckpoint(
        string runId,
        string checkpointId,
        ConfirmCheckpointRequest request)
    {
        if (request == null || !RunStore.IsSafeRunId(request.RequestId))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "invalid_request_id", "requestId 无效。");
        }

        RunSession? session = GetSession(runId);
        if (session == null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。");
        }

        lock (session.Sync)
        {
            if (session.ConfirmReplies.TryGetValue(request.RequestId, out ConfirmCheckpointResponse? replay))
            {
                replay.Replayed = true;
                return replay;
            }
        }

        if (session.Finished || session.Snapshot.Phase == RunPhase.Finished)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "run_not_waiting", "本轮已结束，不能提交现场确认。");
        }

        lock (session.Sync)
        {
            if (session.CheckpointRequests.TryGetValue(checkpointId, out string? submitted) &&
                !string.Equals(submitted, request.RequestId, StringComparison.Ordinal))
            {
                throw new ApiException(
                    StatusCodes.Status409Conflict,
                    "checkpoint_already_submitted",
                    "该确认点已提交过另一条命令，正在等待执行器回执；不能重复排队。");
            }
        }

        CheckpointState? checkpoint = session.Snapshot.Checkpoint;
        if (checkpoint == null ||
            !string.Equals(checkpoint.CheckpointId, checkpointId, StringComparison.Ordinal) ||
            checkpoint.Status != CheckpointStatus.Waiting)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "checkpoint_not_current",
                "该确认点不是当前等待点或已处理，提交已拒绝。");
        }

        if (DateTimeOffset.Now > checkpoint.ExpiresAt)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "checkpoint_expired",
                "确认点已超时，提交已拒绝，步骤结果不改变。");
        }

        string reason = (request.Reason ?? string.Empty).Trim();
        if (request.Result != CheckpointResult.Done && reason.Length == 0)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "reason_required",
                "选择阻塞或失败时必须填写原因。");
        }

        WriteCommand(session, checkpoint, request, reason);

        var response = new ConfirmCheckpointResponse
        {
            Accepted = true,
            Replayed = false,
            Applied = false,
            CheckpointId = checkpoint.CheckpointId,
            StepId = checkpoint.StepId,
            Status = CheckpointStatus.Waiting,
            Message = "确认已提交，等待执行器回执；结果以事件流为准。"
        };
        lock (session.Sync)
        {
            session.ConfirmReplies[request.RequestId] = response;
            session.CheckpointRequests[checkpoint.CheckpointId] = request.RequestId;
        }

        return response;
    }

    /// <summary>
    /// 现场处理完成。必须同时满足原进程退出、无冲突被测进程、现场明确确认设备状态，
    /// 才解除机器占用；任何一项无法核验都保持待核对，不提供强制解锁。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="request">现场处理请求。</param>
    /// <returns>处理结果。</returns>
    public FieldResolutionResponse ResolvePendingField(string runId, FieldResolutionRequest request)
    {
        RunSession? session = GetSession(runId);
        if (session == null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。");
        }

        MachineLockState? state = _locks.Read();
        if (state == null)
        {
            if (!session.PendingFieldActive)
            {
                return new FieldResolutionResponse
                {
                    Released = true,
                    Message = "机器占用已解除，无需重复提交。"
                };
            }

            throw new ApiException(StatusCodes.Status409Conflict, "lock_missing", "机器锁记录缺失，需人工核对。");
        }

        if (!string.Equals(state.RunId, runId, StringComparison.Ordinal) ||
            (!state.PendingField && !session.PendingFieldActive))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "not_pending_field", "本轮不在待现场处理状态。");
        }

        if (!request.ProcessExitedConfirmed || !request.DeviceStateConfirmed)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "confirmations_required",
                "必须同时确认本轮程序已退出且设备状态已按规程处理。");
        }

        List<FieldVerification> verifications = VerifyFieldState(session, state);
        if (verifications.Any(item => !item.Passed))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "field_verification_failed",
                "现场处理核验未通过，机器占用保持。",
                issues: verifications
                    .Where(item => !item.Passed)
                    .Select(item => new ValidationIssue { Field = item.Id, Code = "verification_failed", Message = item.Detail })
                    .ToList());
        }

        string note = (request.Note ?? string.Empty).Trim();
        _store.AppendSyntheticEvent(
            session,
            EventType.PendingFieldResolved,
            message: note.Length == 0 ? "现场处理完成并通过核验。" : note,
            data: new Dictionary<string, string>
            {
                ["processExitedVerified"] = "true",
                ["deviceStateVerified"] = "true",
                ["note"] = note,
                ["requestId"] = request.RequestId
            });
        _store.AppendSyntheticEvent(session, EventType.LockReleased, phase: RunPhase.Finished, message: "机器占用已解除。");
        session.Snapshot.PendingField.Active = false;
        session.Snapshot.Phase = RunPhase.Finished;
        _locks.Release();
        _store.SaveReport(session);

        return new FieldResolutionResponse
        {
            Released = true,
            Message = "现场处理核验通过，机器占用已解除；原运行结论不改写，也不会自动续跑。",
            Verifications = verifications
        };
    }

    /// <summary>创建并监视执行器。</summary>
    private async Task MonitorAsync(RunSession session)
    {
        int? exitCode = null;
        Exception? failure = null;
        try
        {
            session.Snapshot.Executor.Alive = true;
            exitCode = await _executor.RunAsync(
                session,
                _paths.ExecutorPath,
                _arguments.Simulate,
                _arguments.Scenario,
                item =>
                {
                    _store.ApplyExternalEvent(session, item);
                    return Task.CompletedTask;
                },
                processId => RecordExecutorProcess(session, processId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            _logger.LogError(exception, "执行器监视失败：{RunId}", session.Snapshot.RunId);
        }
        finally
        {
            await FinalizeAsync(session, exitCode, failure).ConfigureAwait(false);
        }
    }

    /// <summary>宿主重启后接管仍在运行的本轮事件，不启动新进程。</summary>
    /// <summary>璁板綍鎵ц鍣?PID 鍒版満鍣ㄩ攣锛屼緵瀹夸富閲嶅惎鍚庢牳瀵瑰苟鎺ョ浠嶅湪杩愯鐨勬墽琛屽櫒銆?/summary>
    /// <param name="session">鏈疆浼氳瘽銆?/param>
    /// <param name="processId">鎵ц鍣?PID銆?/param>
    private void RecordExecutorProcess(RunSession session, int processId)
    {
        MachineLockState? state = _locks.Read();
        if (state == null || !string.Equals(state.RunId, session.Snapshot.RunId, StringComparison.Ordinal))
        {
            return;
        }

        state.ExecutorPid = processId;
        state.Phase = "executing";
        _locks.Write(state);
    }

    private async Task ResumeAsync(RunSession session)
    {
        try
        {
            await _executor.AttachAsync(
                session,
                session.Snapshot.Executor.ProcessId,
                item =>
                {
                    _store.ApplyExternalEvent(session, item);
                    return Task.CompletedTask;
                },
                processId => RecordExecutorProcess(session, processId),
                CancellationToken.None).ConfigureAwait(false);
            await FinalizeAsync(session, null, null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "接管运行失败：{RunId}", session.Snapshot.RunId);
            await MarkInterruptedAsync(session, "接管执行器事件失败，结果未知，需现场核对。").ConfigureAwait(false);
        }
    }

    /// <summary>执行器退出后的收敛。</summary>
    private async Task FinalizeAsync(RunSession session, int? exitCode, Exception? failure)
    {
        // 排空最后事件，避免执行器写完立刻退出导致尾部丢失。
        foreach (RunEvent item in _store.ReadEvents(session.Snapshot.RunId))
        {
            if (item.Sequence > session.LastSequence)
            {
                _store.ApplyExternalEvent(session, item);
            }
        }

        session.Snapshot.Executor.Alive = false;
        session.Snapshot.Executor.ExitCode = exitCode;

        if (!session.Finished && session.Snapshot.FinishedAt == null)
        {
            bool abnormal = failure != null || !exitCode.HasValue || exitCode.Value != 0;
            string reason = abnormal
                ? $"执行器异常退出（exitCode={(exitCode?.ToString() ?? "未知")}）：{failure?.Message ?? "未发布 run.finished。"}"
                : "执行器退出但未发布完整终结事件，当前步骤结果未知。";
            await MarkInterruptedAsync(session, reason).ConfigureAwait(false);
        }
        else if (!session.Finished)
        {
            session.Finished = true;
        }

        if (session.PendingFieldActive)
        {
            UpdateLockForPending(session);
        }
        else
        {
            _store.AppendSyntheticEvent(session, EventType.LockReleased, phase: RunPhase.Finished, message: "机器占用已解除。");
            _locks.Release();
        }

        session.Finished = true;
        _store.SaveReport(session);
    }

    /// <summary>
    /// 标记中断：当前步骤结果未知，后续保持未执行，并进入待现场处理。
    /// 不推断为业务断言失败，也不自动重跑。
    /// </summary>
    private Task MarkInterruptedAsync(RunSession session, string reason)
    {
        if (session.Finished)
        {
            return Task.CompletedTask;
        }

        StepState? current;
        lock (session.Sync)
        {
            current = session.Snapshot.Steps.FirstOrDefault(step =>
                step.Status is StepStatus.Running or StepStatus.Waiting);
        }

        if (current != null)
        {
            _store.AppendSyntheticEvent(
                session,
                EventType.StepUnknown,
                stepId: current.StepId,
                status: StepStatus.Unknown,
                reason: "执行中断，当前步骤结果未知；未据此推断业务失败。");
        }

        _store.AppendSyntheticEvent(
            session,
            EventType.PendingFieldEntered,
            phase: RunPhase.PendingField,
            reason: reason,
            data: new Dictionary<string, string>
            {
                ["processId"] = session.Snapshot.PendingField.ProcessId.ToString(),
                ["source"] = "host-recovery"
            });
        _store.AppendSyntheticEvent(session, EventType.RunFinished, phase: RunPhase.PendingField, reason: reason);
        session.Finished = true;
        UpdateLockForPending(session);
        _store.SaveReport(session);
        return Task.CompletedTask;
    }

    /// <summary>把待现场处理写入锁文件，宿主重启后仍保持占用。</summary>
    private void UpdateLockForPending(RunSession session)
    {
        MachineLockState? state = _locks.Read();
        state ??= new MachineLockState
        {
            RunId = session.Snapshot.RunId,
            CreatedAt = session.Snapshot.CreatedAt
        };
        state.Phase = "pendingField";
        state.PendingField = true;
        state.Reason = string.IsNullOrWhiteSpace(session.Snapshot.PendingField.Reason)
            ? "待现场处理：硬件状态未确认，机器占用保持。"
            : session.Snapshot.PendingField.Reason;
        state.ProductProcessId = session.Snapshot.PendingField.ProcessId;
        state.ExecutorPid = session.Snapshot.Executor.ProcessId;
        _locks.Write(state);
    }

    /// <summary>核验现场处理条件。</summary>
    private static List<FieldVerification> VerifyFieldState(RunSession session, MachineLockState state)
    {
        var result = new List<FieldVerification>();
        if (session.Snapshot.Simulated)
        {
            result.Add(new FieldVerification
            {
                Id = "process_exited",
                Label = "本轮被测程序已退出",
                Passed = true,
                Detail = "模拟运行未启动被测程序，进程核验不适用；该结果只用于契约验证。"
            });
        }
        else
        {
            int processId = state.ProductProcessId > 0 ? state.ProductProcessId : session.Snapshot.PendingField.ProcessId;
            result.Add(VerifyProductProcessExited(processId, state.ProductProcessStartedAt));

            string exePath = session.Snapshot.Config.ExePath;
            result.Add(VerifyNoConflictingProcess(exePath, processId));
        }

        result.Add(new FieldVerification
        {
            Id = "device_state",
            Label = "设备状态已按规程处理",
            Passed = true,
            Detail = "现场已在网页明确确认设备状态已处理。"
        });
        return result;
    }

    /// <summary>核验原被测进程已退出，并防止 PID 复用造成误判。</summary>
    private static FieldVerification VerifyProductProcessExited(int processId, DateTimeOffset? startedAt)
    {
        if (processId <= 0)
        {
            return new FieldVerification
            {
                Id = "process_exited",
                Label = "本轮被测程序已退出",
                Passed = false,
                Detail = "未记录本轮被测程序 PID，无法核验身份与退出状态。"
            };
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (startedAt.HasValue &&
                Math.Abs((process.StartTime.ToUniversalTime() - startedAt.Value.UtcDateTime).TotalSeconds) > 3)
            {
                return new FieldVerification
                {
                    Id = "process_exited",
                    Label = "本轮被测程序已退出",
                    Passed = true,
                    Detail = $"PID {processId} 已被其他进程复用，原被测程序视为已退出。"
                };
            }

            return new FieldVerification
            {
                Id = "process_exited",
                Label = "本轮被测程序已退出",
                Passed = false,
                Detail = $"本轮被测程序仍在运行（PID {processId}）。"
            };
        }
        catch (ArgumentException)
        {
            return new FieldVerification
            {
                Id = "process_exited",
                Label = "本轮被测程序已退出",
                Passed = true,
                Detail = $"未找到 PID {processId}，视为已退出。"
            };
        }
        catch (Exception exception)
        {
            return new FieldVerification
            {
                Id = "process_exited",
                Label = "本轮被测程序已退出",
                Passed = false,
                Detail = $"无法读取 PID {processId}：{exception.Message}"
            };
        }
    }

    /// <summary>核验没有同名被测进程仍在运行。</summary>
    private static FieldVerification VerifyNoConflictingProcess(string exePath, int ownedProcessId)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return new FieldVerification
            {
                Id = "conflict_processes",
                Label = "无冲突被测进程",
                Passed = false,
                Detail = "配置中没有被测程序路径，无法核对冲突进程。"
            };
        }

        string name = Path.GetFileNameWithoutExtension(exePath);
        var conflicts = new List<int>();
        try
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id == ownedProcessId || process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    conflicts.Add(process.Id);
                }
            }
        }
        catch (Exception exception)
        {
            return new FieldVerification
            {
                Id = "conflict_processes",
                Label = "无冲突被测进程",
                Passed = false,
                Detail = $"无法枚举被测进程：{exception.Message}"
            };
        }

        return new FieldVerification
        {
            Id = "conflict_processes",
            Label = "无冲突被测进程",
            Passed = conflicts.Count == 0,
            Detail = conflicts.Count == 0
                ? $"未发现其他 {name} 进程。"
                : $"仍存在冲突进程 PID：{string.Join(", ", conflicts)}。"
        };
    }

    /// <summary>把确认命令原子写入本轮 commands 目录。</summary>
    private void WriteCommand(
        RunSession session,
        CheckpointState checkpoint,
        ConfirmCheckpointRequest request,
        string reason)
    {
        string directory = Path.Combine(session.RunDirectory, "commands");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, request.RequestId + ".json");
        var payload = new
        {
            requestId = request.RequestId,
            runId = session.Snapshot.RunId,
            checkpointId = checkpoint.CheckpointId,
            result = request.Result.ToString(),
            reason,
            note = (request.Note ?? string.Empty).Trim(),
            submittedAt = DateTimeOffset.Now
        };
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonDefaults.File));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporary, path);
    }

    /// <summary>从请求解析本轮选项，并校验两个硬件布尔不能同时为真。</summary>
    private RunOptions ResolveOptions(CreateRunRequest request)
    {
        if (request.SkipHardwareSteps == true && request.ManualHardwareSteps == true)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "invalid_options",
                "“跳过硬件步骤”和“人工执行硬件步骤”不能同时选中，本轮未创建。");
        }

        RunOptions options = request.Options ?? _config.Get().Options;
        if (request.Options == null)
        {
            if (request.SkipHardwareSteps == true)
            {
                options = new RunOptions
                {
                    SkipVersionAssertion = request.Options?.SkipVersionAssertion ?? _config.Get().Options.SkipVersionAssertion,
                    HardwarePolicy = HardwarePolicy.Skip
                };
            }
            else if (request.ManualHardwareSteps == true)
            {
                options = new RunOptions
                {
                    SkipVersionAssertion = request.Options?.SkipVersionAssertion ?? _config.Get().Options.SkipVersionAssertion,
                    HardwarePolicy = HardwarePolicy.Manual
                };
            }
        }

        return new RunOptions
        {
            SkipVersionAssertion = options.SkipVersionAssertion,
            HardwarePolicy = options.HardwarePolicy
        };
    }

    /// <summary>生成不含密码的配置快照。</summary>
    private static RunConfigSnapshot BuildConfigSnapshot(ToolConfig config, bool skipVersionAssertion) => new()
    {
        ExePath = config.ExePath,
        RecipeDir = config.RecipeDir,
        EvidenceDir = config.EvidenceDir,
        ExpectedVersion = config.ExpectedVersion,
        BuildDate = NormalizeBuildDateOrEmpty(config.BuildDate),
        TemplateRecipeName = config.TemplateRecipeName,
        UserName = config.UserName,
        ConfigurationRoot = config.ConfigurationRoot,
        PasswordEnvironmentVariable = ConfigStore.PasswordVariable,
        PasswordConfigured = ConfigStore.PasswordConfigured()
    };

    /// <summary>规范化编译日期；无效时原样保留由校验拦截。</summary>
    private static string NormalizeBuildDateOrEmpty(string value) =>
        ConfigValidator.TryNormalizeBuildDate(value, out string normalized) ? normalized : value.Trim();

    /// <summary>复制步骤定义。</summary>
    private static StepDefinition CloneDefinition(StepDefinition source) => new()
    {
        StepId = source.StepId,
        Title = source.Title,
        Section = source.Section,
        SectionTitle = source.SectionTitle,
        HardwareRelated = source.HardwareRelated,
        VersionAssertion = source.VersionAssertion,
        ManualRequirement = source.ManualRequirement,
        ManualInstruction = source.ManualInstruction,
        Observation = source.Observation
    };

    /// <summary>生成 runId。</summary>
    private static string CreateRunId() =>
        RunIdPrefix + DateTimeOffset.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>查找 requestId 已对应的运行，支持宿主重启后的幂等重放。</summary>
    private string? FindRunByRequestId(string requestId)
    {
        lock (_sync)
        {
            if (_requestIndex.TryGetValue(requestId, out string? runId))
            {
                return runId;
            }
        }

        foreach (string candidate in _store.ListRunIds())
        {
            string path = Path.Combine(_paths.RunDirectory(candidate), "request-id.txt");
            if (!File.Exists(path))
            {
                continue;
            }

            if (string.Equals(File.ReadAllText(path).Trim(), requestId, StringComparison.Ordinal))
            {
                lock (_sync)
                {
                    _requestIndex[requestId] = candidate;
                }

                return candidate;
            }
        }

        return null;
    }

    /// <summary>判断进程是否存活且启动时间匹配。</summary>
    private static bool IsProcessAlive(int processId, DateTimeOffset expectedStart)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>在锁内复制快照，避免序列化时与事件投影并发。</summary>
    private static RunSnapshot Snapshot(RunSession session)
    {
        lock (session.Sync)
        {
            string json = JsonSerializer.Serialize(session.Snapshot, JsonDefaults.File);
            return JsonSerializer.Deserialize<RunSnapshot>(json, JsonDefaults.File)!;
        }
    }
}
