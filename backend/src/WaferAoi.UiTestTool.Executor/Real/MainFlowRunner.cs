using System.Text;
using FlaUI.Core.AutomationElements;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 01—07 真实执行主流程（WAOI-FLOW-001）。整条流程共用一个被测进程，按用例编号顺序执行：
/// 01—03 启动/登录/放片、04 新建配方 A、05 晶圆信息、06 建档上片、07 切割道两点角度校正。
///
/// 每条业务结论都来自可观察事实（UIA 属性/页面显示/配方 XML/进程 PID/轴读数/运行提示），
/// 不以固定延时、无异常或“保存成功”代替运行成功；任一步失败或阻塞即停止，后续保持未执行。
/// </summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>本轮清单。</summary>
    private readonly RunManifest _manifest;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>网页确认点。</summary>
    private readonly CheckpointGate _checkpoints;

    /// <summary>证据与日志。</summary>
    private readonly ExecutorArtifacts _artifacts;

    /// <summary>真实执行配置。</summary>
    private readonly ExecutorSettings _settings;

    /// <summary>本轮硬件策略。</summary>
    private readonly HardwarePolicy _policy;

    /// <summary>步骤记录，按清单顺序。</summary>
    private readonly List<StepRecord> _records;

    /// <summary>被测程序驱动；01.01 启动后创建。</summary>
    private WaferAoiApplication? _application;

    /// <summary>绑定本轮进程的 UI 操作器。</summary>
    private OwnedUi _ui = null!;

    /// <summary>启动流程。</summary>
    private SoftwareStartupFlow _startup = null!;

    /// <summary>02.01 登录后的主窗口。</summary>
    private Window _main = null!;

    /// <summary>配方编辑页对象。</summary>
    private RecipeEditorPage _recipeEditor = null!;

    /// <summary>新建配方窗口。</summary>
    private NewRecipeDialog _newRecipe = null!;

    /// <summary>晶圆信息页对象。</summary>
    private WaferInformationPage _waferInformation = null!;

    /// <summary>07 角度校正页对象。</summary>
    private StreetAlignmentPage _street = null!;

    /// <summary>本轮配方 A 名称；只生成一次。</summary>
    private string _recipeAName = string.Empty;

    /// <summary>03.01 记录的本轮开始时间。</summary>
    private DateTimeOffset _runStartedAt;

    /// <summary>07.12／07.14 记录的两点轴位。</summary>
    private Axes _point1;
    private Axes _point2;

    /// <summary>是否已驱动过硬件（回零/上片/运动）。用于判断失败后是否需要保留进程待现场处理。</summary>
    private bool _hardwareActuated;

    /// <summary>停止时的结论。</summary>
    private StepStatus? _stoppedStatus;

    /// <summary>停止原因。</summary>
    private string _stopReason = string.Empty;

    /// <summary>创建运行器。</summary>
    /// <param name="manifest">本轮清单。</param>
    /// <param name="writer">事件出口。</param>
    /// <param name="checkpoints">网页确认点。</param>
    /// <param name="artifacts">证据收集器。</param>
    /// <param name="settings">执行配置。</param>
    public MainFlowRunner(
        RunManifest manifest,
        EventWriter writer,
        CheckpointGate checkpoints,
        ExecutorArtifacts artifacts,
        ExecutorSettings settings)
    {
        _manifest = manifest;
        _writer = writer;
        _checkpoints = checkpoints;
        _artifacts = artifacts;
        _settings = settings;
        _policy = manifest.Options.HardwarePolicy;
        _records = manifest.Steps
            .Select(step => new StepRecord(step.StepId, step.Title))
            .ToList();
    }

    /// <summary>执行整轮主流程并收敛事件；任何异常都不会被包装成通过。</summary>
    public void Run()
    {
        _writer.Emit(EventType.RunPreparing, phase: RunPhase.Preparing, message: "开始启动前检查。");
        IReadOnlyList<string> problems = _settings.CheckPrerequisites();
        _writer.Emit(
            EventType.RunPrepared,
            phase: RunPhase.Preparing,
            message: problems.Count == 0
                ? "启动前检查通过。"
                : "启动前检查未通过：" + string.Join("；", problems),
            data: new Dictionary<string, string>
            {
                ["ok"] = problems.Count == 0 ? "true" : "false",
                ["hardwarePolicy"] = _policy.ToString(),
                ["skipVersionAssertion"] = _manifest.Options.SkipVersionAssertion ? "true" : "false",
                ["problemCount"] = problems.Count.ToString()
            });

        _writer.Emit(EventType.RunStarted, phase: RunPhase.Executing, message: "开始执行 01—07 节主流程。");

        if (problems.Count > 0)
        {
            BlockFirstStep(string.Join("；", problems));
        }
        else
        {
            ExecuteSections();
        }

        Finish();
    }

    /// <summary>按章节顺序执行；01.02 依赖登录后的主窗口标题，保留原编号延后核对。</summary>
    private void ExecuteSections()
    {
        RunStartupSection();
        if (_stoppedStatus != null) return;
        RunRecipeSection();
        if (_stoppedStatus != null) return;
        RunWaferInformationSection();
        if (_stoppedStatus != null) return;
        RunWaferToStageSection();
        if (_stoppedStatus != null) return;
        RunStreetAlignmentSection();
    }

    /// <summary>执行一个步骤；失败或阻塞立即停止本轮。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="body">步骤主体。</param>
    private void Step(string stepId, Action<StepContext> body)
    {
        if (_stoppedStatus != null)
        {
            return;
        }

        StepRecord record = GetRecord(stepId);
        var context = new StepContext(this, record);
        record.StartedAt = DateTimeOffset.Now;
        _writer.Emit(EventType.StepStarted, stepId: stepId, phase: RunPhase.Executing, status: StepStatus.Running);
        _artifacts.Log($"开始步骤：{stepId} {record.Title}");

        bool collectFailure = false;
        try
        {
            body(context);
            if (record.Status == StepStatus.NotRun)
            {
                record.Status = StepStatus.Passed;
            }
        }
        catch (StepVerdictException verdict)
        {
            record.Status = verdict.Status;
            record.Reason = verdict.Message;
            record.Notes.Add(verdict.Message);
            // 失败与阻塞都采集原始证据（UIA 树/截图/进程状态），便于现场一次补齐定位。
            collectFailure = verdict.Status is StepStatus.Failed or StepStatus.Blocked;
        }
        catch (OperationCanceledException)
        {
            record.Status = StepStatus.Blocked;
            record.Reason = "执行被取消。";
            record.Notes.Add(record.Reason);
        }
        catch (Exception exception)
        {
            record.Status = StepStatus.Failed;
            record.Reason = $"{exception.GetType().Name}: {exception.Message}";
            record.Notes.Add(record.Reason);
            collectFailure = true;
        }
        finally
        {
            record.FinishedAt = DateTimeOffset.Now;
        }

        if (collectFailure)
        {
            _artifacts.CollectFailure(_application, new Exception(record.Reason), stepId);
        }

        _writer.Emit(
            EventType.StepFinished,
            stepId: stepId,
            phase: RunPhase.Executing,
            status: record.Status,
            reason: record.Reason.Length == 0 ? null : record.Reason);
        _artifacts.Log($"{stepId} {record.Title}：{DisplayName(record.Status)}" +
                       (record.Reason.Length == 0 ? string.Empty : "；" + record.Reason));

        if (record.Status is StepStatus.Failed or StepStatus.Blocked)
        {
            _stoppedStatus = record.Status;
            _stopReason = record.Reason;
        }
    }

    /// <summary>启动前检查失败时把 01.01 记为阻塞，其余步骤保持未执行。</summary>
    /// <param name="reason">阻塞原因。</param>
    private void BlockFirstStep(string reason)
    {
        StepRecord first = _records[0];
        first.StartedAt = DateTimeOffset.Now;
        _writer.Emit(EventType.StepStarted, stepId: first.StepId, phase: RunPhase.Executing, status: StepStatus.Running);
        first.Status = StepStatus.Blocked;
        first.Reason = reason;
        first.Notes.Add(reason);
        first.FinishedAt = DateTimeOffset.Now;
        _writer.Emit(
            EventType.StepFinished,
            stepId: first.StepId,
            status: StepStatus.Blocked,
            reason: reason);
        _artifacts.Log($"{first.StepId} {first.Title}：阻塞；{reason}");
        _stoppedStatus = StepStatus.Blocked;
        _stopReason = reason;
    }

    /// <summary>本轮收敛：保留进程/清理的判定、步骤报告与结束事件。</summary>
    private void Finish()
    {
        string stepsReport = WriteStepReport();
        _artifacts.Register(stepsReport, "report", "text/plain", null, "主流程步骤结果");

        bool keepProcess = _stoppedStatus != null
                           && _hardwareActuated
                           && _application != null
                           && _application.ProcessId > 0
                           && _application.IsRunning;

        if (keepProcess)
        {
            _artifacts.CaptureScreen("pending-field-screen", null);
            _writer.Emit(
                EventType.PendingFieldEntered,
                phase: RunPhase.PendingField,
                reason: "硬件状态未确认：保留本轮被测程序，等待现场处理并在网页核验后解锁。",
                data: new Dictionary<string, string>
                {
                    ["processId"] = _application!.ProcessId.ToString(),
                    ["processStartedAt"] = _application.StartedAt?.ToString("O") ?? string.Empty,
                    ["hardwareActuated"] = "true",
                    ["stoppedStepStatus"] = _stoppedStatus.ToString() ?? string.Empty
                });
            _artifacts.Log("硬件已动作且本轮未正常收敛：保留被测程序，进入待现场处理。");
            _artifacts.RegisterStepLog(null);
            _writer.Emit(
                EventType.RunFinished,
                phase: RunPhase.PendingField,
                reason: "待现场处理：硬件状态未确认，机器占用保持。" + Suffix(_stopReason));
            return;
        }

        _writer.Emit(EventType.RunPhase, phase: RunPhase.Cleaning, message: "进入清理阶段。");
        _writer.Emit(EventType.CleanupStarted, phase: RunPhase.Cleaning, message: "开始清理本轮资源。");
        string cleanupStatus = "succeeded";
        string cleanupReason = "未启动被测程序，无需结束进程。";
        try
        {
            if (_application != null)
            {
                int processId = _application.ProcessId;
                _application.Kill();
                cleanupReason = processId > 0
                    ? $"已结束本轮被测程序 PID={processId}；配方与证据保留。"
                    : cleanupReason;
            }

            _application?.Dispose();
            _application = null;
        }
        catch (Exception exception)
        {
            cleanupStatus = "failed";
            cleanupReason = "清理失败：" + exception.Message;
        }

        _writer.Emit(
            EventType.CleanupFinished,
            phase: RunPhase.Cleaning,
            message: cleanupReason,
            data: new Dictionary<string, string> { ["status"] = cleanupStatus });
        _artifacts.RegisterStepLog(null);
        _writer.Emit(
            EventType.RunFinished,
            phase: RunPhase.Finished,
            reason: (_stoppedStatus == null ? "01—07 节全部执行完成。" : "已在失败或阻塞处停止。") + Suffix(_stopReason));
    }

    /// <summary>步骤报告：逐行列出编号、标题、状态与说明。</summary>
    /// <returns>报告文件路径。</returns>
    private string WriteStepReport()
    {
        var lines = new List<string>
        {
            $"用例 WAOI-FLOW-001；范围 01—07 节共 {_records.Count} 步；硬件策略={_policy}；" +
            $"跳过版本断言={(_manifest.Options.SkipVersionAssertion ? "是" : "否")}。",
            "说明：人工硬件跳过与按用户要求跳过都不代表硬件通过；本报告只记录实际观察到的事实。",
            "统计：" + string.Join(
                "，",
                _records.GroupBy(record => record.Status)
                    .OrderBy(group => group.Key)
                    .Select(group => DisplayName(group.Key) + "=" + group.Count()))
        };
        foreach (StepRecord record in _records)
        {
            string notes = record.Notes.Count == 0 ? string.Empty : "；" + string.Join("；", record.Notes);
            lines.Add($"{record.StepId} {record.Title}：{DisplayName(record.Status)}{notes}");
        }

        lines.Add($"证据目录：{_artifacts.ArtifactDirectory}");
        string path = Path.Combine(_artifacts.ArtifactDirectory, "主流程步骤结果.txt");
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(true));
        return path;
    }

    /// <summary>等待网页确认。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="instruction">现场说明。</param>
    /// <returns>现场结论。</returns>
    internal CheckpointOutcome WaitForOperator(string stepId, string instruction) =>
        _checkpoints.WaitAsync(stepId, instruction, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>写一份不含密码的执行日志。</summary>
    /// <param name="message">日志文本。</param>
    internal void Log(string message) => _artifacts.Log(message);

    /// <summary>取步骤记录。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <returns>记录。</returns>
    private StepRecord GetRecord(string stepId)
    {
        StepRecord? record = _records.FirstOrDefault(item =>
            string.Equals(item.StepId, stepId, StringComparison.Ordinal));
        return record ?? throw new ArgumentException("步骤 " + stepId + " 不在本轮清单中。", nameof(stepId));
    }

    /// <summary>状态中文名。</summary>
    /// <param name="status">状态。</param>
    /// <returns>中文名称。</returns>
    private static string DisplayName(StepStatus status) => status switch
    {
        StepStatus.NotRun => "未执行",
        StepStatus.Running => "执行中",
        StepStatus.Waiting => "等待现场确认",
        StepStatus.Passed => "通过",
        StepStatus.OperatorConfirmed => "通过（人工确认）",
        StepStatus.PassedWithHardwareSkipped => "通过（含人工硬件跳过）",
        StepStatus.HardwareSkipped => "人工硬件跳过",
        StepStatus.UserSkipped => "按用户要求跳过",
        StepStatus.Blocked => "阻塞",
        StepStatus.Unknown => "中断·结果未知",
        _ => "失败"
    };

    /// <summary>拼接原因后缀。</summary>
    /// <param name="reason">原因。</param>
    /// <returns>可读后缀。</returns>
    private static string Suffix(string reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : "原因：" + reason;

    /// <summary>是否跳过硬件策略。</summary>
    private bool SkipHardware => _policy == HardwarePolicy.Skip;

    /// <summary>是否人工执行硬件策略。</summary>
    private bool ManualHardware => _policy == HardwarePolicy.Manual;

    /// <summary>是否严格策略（三项全不选）。</summary>
    private bool StrictHardware => _policy == HardwarePolicy.Strict;
}
