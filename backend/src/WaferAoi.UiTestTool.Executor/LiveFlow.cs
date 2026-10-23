using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 真实 UIA 主流程入口。当前切片只完成独立生命周期、事件、确认与清理协议；
/// 27 步的页面/驱动闭包迁移（T04—T05）尚未落地，因此显式以阻塞结束，
/// 不启动被测程序、不写成功结论，也不把能力缺失包装成通过。
/// </summary>
internal sealed class LiveFlow : IExecutorFlow
{
    /// <summary>本轮清单。</summary>
    private readonly RunManifest _manifest;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>创建真实流程入口。</summary>
    /// <param name="manifest">本轮清单。</param>
    /// <param name="writer">事件出口。</param>
    public LiveFlow(RunManifest manifest, EventWriter writer)
    {
        _manifest = manifest;
        _writer = writer;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        _writer.Emit(EventType.RunPreparing, phase: RunPhase.Preparing, message: "开始真实执行前检查。");

        const string reason =
            "真实 UIA 主流程尚未迁移到独立执行器（T04—T05 未完成）：未启动被测程序，本轮不产生实机结论。";

        _writer.Emit(EventType.Log, message: reason);
        _writer.Emit(
            EventType.RunPrepared,
            phase: RunPhase.Preparing,
            reason: reason,
            data: new Dictionary<string, string>
            {
                ["capabilityGap"] = "live-flow-not-migrated"
            });
        _writer.Emit(EventType.CleanupFinished, phase: RunPhase.Finished, message: "未启动被测程序，无需清理。", data: new Dictionary<string, string> { ["status"] = "notRequired" });
        _writer.Emit(EventType.RunFinished, phase: RunPhase.Finished, reason: reason);
        return Task.CompletedTask;
    }
}
