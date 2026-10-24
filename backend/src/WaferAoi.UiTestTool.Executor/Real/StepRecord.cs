using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 单步执行记录：状态、原因与说明。报告、事件与网页状态都从同一份记录派生。
/// </summary>
internal sealed class StepRecord
{
    /// <summary>创建步骤记录。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="title">步骤标题。</param>
    public StepRecord(string stepId, string title)
    {
        StepId = stepId;
        Title = title;
    }

    /// <summary>步骤编号。</summary>
    public string StepId { get; }

    /// <summary>步骤标题。</summary>
    public string Title { get; }

    /// <summary>当前状态。</summary>
    public StepStatus Status { get; set; } = StepStatus.NotRun;

    /// <summary>结论原因；为空表示通过。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>补充说明。</summary>
    public List<string> Notes { get; } = new();

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>
/// 步骤上下文：为步骤主体提供说明、跳过与网页确认能力，屏蔽事件与策略细节。
/// </summary>
internal sealed class StepContext
{
    /// <summary>所属运行。</summary>
    private readonly MainFlowRunner _runner;

    /// <summary>创建上下文。</summary>
    /// <param name="runner">运行器。</param>
    /// <param name="record">步骤记录。</param>
    public StepContext(MainFlowRunner runner, StepRecord record)
    {
        _runner = runner;
        Record = record;
    }

    /// <summary>步骤记录。</summary>
    public StepRecord Record { get; }

    /// <summary>追加说明。</summary>
    /// <param name="note">说明文字。</param>
    public void Note(string note)
    {
        Record.Notes.Add(note);
        _runner.Log(Record.StepId + " " + note);
    }

    /// <summary>记录本步骤部分硬件检查被跳过（软件断言仍需通过）。</summary>
    /// <param name="reason">被跳过的硬件检查。</param>
    public void SkipHardwarePart(string reason) =>
        Weaken(StepStatus.PassedWithHardwareSkipped, "人工硬件跳过：" + reason);

    /// <summary>记录整步为硬件操作并跳过。</summary>
    /// <param name="reason">被跳过的操作。</param>
    public void SkipWholeStep(string reason) =>
        Weaken(StepStatus.HardwareSkipped, "人工硬件跳过：" + reason);

    /// <summary>记录按本轮选项明确跳过。</summary>
    /// <param name="reason">跳过原因。</param>
    public void SkipByUser(string reason) => Weaken(StepStatus.UserSkipped, reason);

    /// <summary>记录现场已操作并确认。</summary>
    /// <param name="note">现场备注。</param>
    public void ConfirmedByOperator(string note) =>
        Weaken(StepStatus.OperatorConfirmed, "现场已操作并确认" + (note.Length == 0 ? "。" : "：" + note));

    /// <summary>请求网页确认；返回现场结论。</summary>
    /// <param name="instruction">现场操作说明。</param>
    /// <returns>现场结论。</returns>
    public CheckpointOutcome Confirm(string instruction)
    {
        CheckpointOutcome outcome = _runner.WaitForOperator(Record.StepId, instruction);
        switch (outcome.Result)
        {
            case CheckpointResult.Done:
                ConfirmedByOperator(string.IsNullOrWhiteSpace(outcome.Reason) ? string.Empty : outcome.Reason);
                break;
            case CheckpointResult.Failed:
                throw Verify.Failed(string.IsNullOrWhiteSpace(outcome.Reason) ? "现场确认失败。" : outcome.Reason);
            default:
                throw Verify.Blocked(
                    outcome.TimedOut
                        ? "等待现场确认超时，未获得现场结论。"
                        : "现场确认阻塞：" + (string.IsNullOrWhiteSpace(outcome.Reason) ? "未填写原因。" : outcome.Reason));
        }

        return outcome;
    }

    /// <summary>按“结论只减弱不增强”的规则更新状态。</summary>
    /// <param name="status">新结论。</param>
    /// <param name="reason">说明。</param>
    private void Weaken(StepStatus status, string reason)
    {
        if (status > Record.Status)
        {
            Record.Status = status;
        }

        Record.Reason = Record.Reason.Length == 0 ? reason : Record.Reason + "；" + reason;
        Record.Notes.Add(reason);
        _runner.Log(Record.StepId + " " + reason);
    }
}
