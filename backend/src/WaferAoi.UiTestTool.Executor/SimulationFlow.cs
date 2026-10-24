using System.Text;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 契约验证与联调用的确定性流程。它不操作被测程序、不访问设备，
/// 生成的事件与页面分支用于验证选项、停止、确认、清理和待现场处理；不得标记为实机通过。
/// </summary>
internal sealed class SimulationFlow : IExecutorFlow
{
    /// <summary>本轮清单。</summary>
    private readonly RunManifest _manifest;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>确认点。</summary>
    private readonly CheckpointGate _checkpoints;

    /// <summary>场景名。</summary>
    private readonly string _scenario;

    /// <summary>创建模拟流程。</summary>
    public SimulationFlow(RunManifest manifest, EventWriter writer, CheckpointGate checkpoints)
    {
        _manifest = manifest;
        _writer = writer;
        _checkpoints = checkpoints;
        _scenario = (manifest.Scenario ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _writer.Emit(EventType.RunPreparing, phase: RunPhase.Preparing, message: "开始启动前检查（模拟）。");
        _writer.Emit(
            EventType.RunPrepared,
            phase: RunPhase.Preparing,
            message: "启动前检查完成（模拟，不代表实机条件已满足）。",
            data: new Dictionary<string, string>
            {
                ["simulated"] = "true",
                ["passwordConfigured"] = _manifest.Config.PasswordConfigured ? "true" : "false"
            });
        _writer.Emit(EventType.RunStarted, phase: RunPhase.Executing, message: "开始执行本期 45 步（模拟）。");

        string stepsLog = Path.Combine(_manifest.RunDir, "artifacts", "steps.log");
        File.WriteAllText(stepsLog, $"{DateTimeOffset.Now:O} 模拟运行 {_manifest.RunId} 开始。\r\n", new UTF8Encoding(false));
        _writer.RegisterArtifact(stepsLog, "log", "text/plain", reason: "模拟运行日志");

        StepStatus? stoppedAt = null;
        string stopReason = string.Empty;

        foreach (StepDefinition step in _manifest.Steps)
        {
            if (stoppedAt != null)
            {
                break;
            }

            _writer.Emit(EventType.StepStarted, stepId: step.StepId, phase: RunPhase.Executing, status: StepStatus.Running);

            StepStatus outcome;
            string reason = string.Empty;

            if (_manifest.Options.SkipVersionAssertion && step.VersionAssertion)
            {
                outcome = StepStatus.UserSkipped;
                reason = "本轮选择跳过版本断言（版本号及编译日期）。";
            }
            else if (_scenario == "fail" && step.StepId == "04.03")
            {
                outcome = StepStatus.Failed;
                reason = "模拟断言失败：配方名称回读不一致。";
                string exceptionPath = Path.Combine(_manifest.RunDir, "artifacts", "exception.txt");
                File.WriteAllText(exceptionPath, reason, new UTF8Encoding(false));
                _writer.RegisterArtifact(exceptionPath, "text", "text/plain", step.StepId, "模拟失败证据");
            }
            else if (_manifest.Options.HardwarePolicy == HardwarePolicy.Skip && step.HardwareRelated)
            {
                outcome = IsMixedHardwareStep(step.StepId)
                    ? StepStatus.PassedWithHardwareSkipped
                    : StepStatus.HardwareSkipped;
                reason = "本轮选择跳过硬件步骤：硬件动作或硬件状态核对未执行。";
            }
            else if (RequiresCheckpoint(step))
            {
                CheckpointOutcome checkpoint = await _checkpoints
                    .WaitAsync(step.StepId, BuildInstruction(step), cancellationToken)
                    .ConfigureAwait(false);
                if (checkpoint.Result == CheckpointResult.Done)
                {
                    outcome = StepStatus.OperatorConfirmed;
                    reason = string.IsNullOrWhiteSpace(checkpoint.Reason)
                        ? "现场已操作并确认。"
                        : checkpoint.Reason;
                }
                else if (checkpoint.Result == CheckpointResult.Failed)
                {
                    outcome = StepStatus.Failed;
                    reason = string.IsNullOrWhiteSpace(checkpoint.Reason) ? "现场确认失败。" : checkpoint.Reason;
                }
                else
                {
                    outcome = StepStatus.Blocked;
                    reason = string.IsNullOrWhiteSpace(checkpoint.Reason) ? "现场确认阻塞。" : checkpoint.Reason;
                }
            }
            else if (step.HardwareRelated && _manifest.Options.HardwarePolicy == HardwarePolicy.Manual)
            {
                // 人工策略下，硬件相关但非整步人工的步骤仍自动执行软件部分；硬件部分由能力表逐步迁移。
                outcome = StepStatus.Passed;
            }
            else
            {
                outcome = StepStatus.Passed;
            }

            _writer.Emit(
                EventType.StepFinished,
                stepId: step.StepId,
                phase: RunPhase.Executing,
                status: outcome,
                reason: reason);

            if (outcome == StepStatus.Failed || outcome == StepStatus.Blocked)
            {
                stoppedAt = outcome;
                stopReason = reason;
            }

            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        if (_scenario == "pending-field")
        {
            // 模拟“硬件状态未确认”：不进入自动清理，也不写 lock.released；占用由 Web 保持。
            _writer.Emit(
                EventType.PendingFieldEntered,
                phase: RunPhase.PendingField,
                reason: "模拟硬件状态未确认：保留被测程序，等待现场处理。",
                data: new Dictionary<string, string> { ["simulated"] = "true" });
            _writer.Emit(
                EventType.RunFinished,
                phase: RunPhase.PendingField,
                reason: "待现场处理：硬件状态未确认，机器占用保持。");
            return;
        }

        _writer.Emit(EventType.RunPhase, phase: RunPhase.Cleaning, message: "进入清理阶段。");
        _writer.Emit(EventType.CleanupStarted, phase: RunPhase.Cleaning, message: "开始清理（模拟）。");
        _writer.Emit(EventType.CleanupFinished, phase: RunPhase.Cleaning, message: "清理完成（模拟）。", data: new Dictionary<string, string> { ["status"] = "succeeded" });
        _writer.Emit(EventType.RunFinished, phase: RunPhase.Finished, reason: stopReason);
    }

    /// <summary>判断步骤是否含可独立执行的软件断言。跳过硬件时记为“跳过（部分硬件）”。</summary>
    private static bool IsMixedHardwareStep(string stepId) =>
        stepId is "01.01" or "02.02" or "03.02" or "04.01" or "06.01" or "06.03";

    /// <summary>判断当前步骤是否需要网页确认。</summary>
    private bool RequiresCheckpoint(StepDefinition step)
    {
        if (step.ManualRequirement == "always")
        {
            return true;
        }

        if (step.ManualRequirement == "hardwarePolicyOnly")
        {
            return _manifest.Options.HardwarePolicy == HardwarePolicy.Manual || _scenario == "manual";
        }

        return false;
    }

    /// <summary>生成现场说明；优先使用清单中的原用例说明。</summary>
    private static string BuildInstruction(StepDefinition step) =>
        string.IsNullOrWhiteSpace(step.ManualInstruction)
            ? "请按原用例完成现场操作后确认。"
            : step.ManualInstruction;
}
