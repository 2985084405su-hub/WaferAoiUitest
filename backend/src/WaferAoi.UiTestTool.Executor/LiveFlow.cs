using WaferAoi.UiTestTool.Contracts;
using WaferAoi.UiTestTool.Executor.Real;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 真实 UIA 主流程入口：按 manifest 快照启动被测程序并执行 01—07 节 45 步。
/// 不写成功结论、不把缺控件包装成通过；任何失败或阻塞都会停止本轮并保留证据。
/// </summary>
internal sealed class LiveFlow : IExecutorFlow
{
    /// <summary>本轮清单。</summary>
    private readonly RunManifest _manifest;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>网页确认点。</summary>
    private readonly CheckpointGate _checkpoints;

    /// <summary>创建真实执行流程。</summary>
    /// <param name="manifest">本轮清单。</param>
    /// <param name="writer">事件出口。</param>
    /// <param name="checkpoints">网页确认点。</param>
    public LiveFlow(RunManifest manifest, EventWriter writer, CheckpointGate checkpoints)
    {
        _manifest = manifest;
        _writer = writer;
        _checkpoints = checkpoints;
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken)
    {
        ExecutorSettings settings = ExecutorSettings.FromManifest(_manifest);
        var artifacts = new ExecutorArtifacts(_manifest.RunDir, _writer);
        var runner = new MainFlowRunner(_manifest, _writer, _checkpoints, artifacts, settings);
        runner.Run();
        return Task.CompletedTask;
    }
}
