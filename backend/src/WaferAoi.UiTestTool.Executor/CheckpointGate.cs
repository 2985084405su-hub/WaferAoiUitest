using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>现场确认结果。</summary>
internal sealed class CheckpointOutcome
{
    /// <summary>现场结果；超时按阻塞处理。</summary>
    public CheckpointResult Result { get; set; } = CheckpointResult.Blocked;

    /// <summary>原因或备注。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>确认点标识。</summary>
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>是否超时。</summary>
    public bool TimedOut { get; set; }
}

/// <summary>
/// 网页人工确认点。执行器创建带唯一 checkpointId 的等待点，轮询命令通道；
/// 超时、重复和归属不符的提交都不会推进步骤。
/// </summary>
internal sealed class CheckpointGate
{
    /// <summary>本轮清单。</summary>
    private readonly RunManifest _manifest;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>命令通道。</summary>
    private readonly CommandChannel _channel;

    /// <summary>等待超时。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>创建确认点。</summary>
    /// <param name="manifest">本轮清单。</param>
    /// <param name="writer">事件出口。</param>
    /// <param name="channel">命令通道。</param>
    /// <param name="timeout">等待超时。</param>
    public CheckpointGate(RunManifest manifest, EventWriter writer, CommandChannel channel, TimeSpan timeout)
    {
        _manifest = manifest;
        _writer = writer;
        _channel = channel;
        _timeout = timeout;
    }

    /// <summary>
    /// 创建确认点并等待现场提交；不因页面刷新重新计时。
    /// </summary>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="instruction">现场操作说明。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>现场结果。</returns>
    public async Task<CheckpointOutcome> WaitAsync(
        string stepId,
        string instruction,
        CancellationToken cancellationToken)
    {
        string checkpointId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.Now;
        DateTimeOffset expiresAt = startedAt.Add(_timeout);

        _writer.Emit(
            EventType.CheckpointCreated,
            stepId: stepId,
            phase: RunPhase.WaitingHuman,
            status: StepStatus.Waiting,
            checkpointId: checkpointId,
            message: instruction,
            data: new Dictionary<string, string>
            {
                ["startedAt"] = startedAt.ToString("O"),
                ["expiresAt"] = expiresAt.ToString("O"),
                ["instruction"] = instruction
            });

        _writer.Emit(EventType.StepWaiting, stepId: stepId, phase: RunPhase.WaitingHuman, checkpointId: checkpointId, reason: instruction);

        while (!cancellationToken.IsCancellationRequested)
        {
            CommandEnvelope? command = _channel.TryRead(checkpointId);
            if (command != null)
            {
                string reason = (command.Reason ?? string.Empty).Trim();
                string note = (command.Note ?? string.Empty).Trim();
                if (command.Result != CheckpointResult.Done && reason.Length == 0)
                {
                    // 阻塞或失败必须带原因；缺失时拒绝本次命令并继续等待，不改变步骤结果。
                    _channel.WriteAck(command.RequestId, false, "reason_required", "阻塞或失败必须填写原因。", false);
                }
                else
                {
                    _channel.WriteAck(command.RequestId, true, "applied", "现场确认已应用。", true);
                    _writer.Emit(
                        EventType.CheckpointResolved,
                        stepId: stepId,
                        status: StepStatus.Waiting,
                        checkpointId: checkpointId,
                        message: note.Length == 0 ? reason : note,
                        data: new Dictionary<string, string>
                        {
                            ["result"] = command.Result.ToString(),
                            ["reason"] = reason,
                            ["note"] = note,
                            ["requestId"] = command.RequestId
                        });
                    return new CheckpointOutcome
                    {
                        Result = command.Result,
                        Reason = reason,
                        CheckpointId = checkpointId
                    };
                }
            }

            if (DateTimeOffset.Now >= expiresAt)
            {
                _writer.Emit(
                    EventType.CheckpointResolved,
                    stepId: stepId,
                    status: StepStatus.Waiting,
                    checkpointId: checkpointId,
                    message: "等待现场确认超时。",
                    data: new Dictionary<string, string>
                    {
                        ["result"] = "timeout",
                        ["reason"] = "等待现场确认超时。"
                    });
                return new CheckpointOutcome
                {
                    Result = CheckpointResult.Blocked,
                    Reason = "等待现场确认超时，未获得现场结论。",
                    CheckpointId = checkpointId,
                    TimedOut = true
                };
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CheckpointOutcome { Result = CheckpointResult.Blocked, Reason = "确认等待已取消。" };
    }
}

/// <summary>执行器主流程接口。</summary>
internal interface IExecutorFlow
{
    /// <summary>执行本轮并写入结构事件。</summary>
    /// <param name="cancellationToken">取消标记。</param>
    Task RunAsync(CancellationToken cancellationToken);
}
