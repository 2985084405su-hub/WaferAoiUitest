namespace WaferAoi.UiTestTool.Contracts;

/// <summary>
/// 步骤状态。取值与需求 7.1 的现有源码状态一一对应，前端只做展示映射，不二次推导。
/// </summary>
public enum StepStatus
{
    /// <summary>尚未执行；不代表跳过。</summary>
    NotRun,

    /// <summary>正在执行；等待现场确认时另由 <see cref="Waiting"/> 表示。</summary>
    Running,

    /// <summary>正在等待现场人员在网页确认；不计入任何终态。</summary>
    Waiting,

    /// <summary>自动断言通过。</summary>
    Passed,

    /// <summary>现场人员操作并确认，其余自动断言通过；不能显示为全自动完成。</summary>
    OperatorConfirmed,

    /// <summary>软件断言通过，但步骤内部分硬件检查被跳过。</summary>
    PassedWithHardwareSkipped,

    /// <summary>整步都是硬件操作，本轮按策略跳过。</summary>
    HardwareSkipped,

    /// <summary>本轮选项明确要求跳过（目前只用于 01.02 跳过版本断言）。</summary>
    UserSkipped,

    /// <summary>前提或控件不可用，无法给出业务结论。</summary>
    Blocked,

    /// <summary>业务断言失败。</summary>
    Failed,

    /// <summary>执行器异常终止且该步没有终态事件；结果未知，不推断为失败。</summary>
    Unknown
}

/// <summary>
/// 本轮硬件策略。两个硬件复选框在后端转换为单一枚举，避免保存互相矛盾的布尔值。
/// </summary>
public enum HardwarePolicy
{
    /// <summary>两项都不选：严格按原用例执行硬件操作、等待与断言。</summary>
    Strict,

    /// <summary>跳过硬件步骤；混合步骤仍执行可独立完成的软件断言。</summary>
    Skip,

    /// <summary>硬件操作点等待现场执行并在网页确认。</summary>
    Manual
}

/// <summary>
/// 运行阶段。准备、执行、等待人工、清理、结束分别可见。
/// </summary>
public enum RunPhase
{
    /// <summary>已创建，尚未开始准备检查。</summary>
    Created,

    /// <summary>正在执行启动前检查。</summary>
    Preparing,

    /// <summary>准备完成，正在驱动被测程序。</summary>
    Executing,

    /// <summary>正在等待现场人员确认。</summary>
    WaitingHuman,

    /// <summary>步骤结束后正在清理。</summary>
    Cleaning,

    /// <summary>硬件状态未确认，等待现场处理；机器占用保持。</summary>
    PendingField,

    /// <summary>本轮已收敛，可查看记录。</summary>
    Finished
}

/// <summary>
/// 本轮结论。清理状态与本结论分开保存。
/// </summary>
public enum RunConclusion
{
    /// <summary>尚未形成结论。</summary>
    Pending,

    /// <summary>45 步全部成功且清理通过。</summary>
    Passed,

    /// <summary>存在跳过项，但没有失败或阻塞。</summary>
    PassedWithSkips,

    /// <summary>存在阻塞，且没有失败。</summary>
    Blocked,

    /// <summary>存在失败。</summary>
    Failed,

    /// <summary>执行器异常退出或状态无法可靠判断。</summary>
    Interrupted,

    /// <summary>硬件状态未确认，待现场处理。</summary>
    PendingField
}

/// <summary>
/// 现场确认结果。网页只提供这三个选项，不提供强制通过。
/// </summary>
public enum CheckpointResult
{
    /// <summary>现场已按说明完成，后续软件断言仍需执行。</summary>
    Done,

    /// <summary>现场条件不满足，本轮阻塞。</summary>
    Blocked,

    /// <summary>现场操作确认失败，本轮失败。</summary>
    Failed
}

/// <summary>
/// 结构化事件类型。事件先落盘再推送，同一 runId 只有一个有序出口。
/// </summary>
public enum EventType
{
    /// <summary>运行已创建，包含选项快照。</summary>
    RunCreated,

    /// <summary>开始准备检查。</summary>
    RunPreparing,

    /// <summary>准备检查完成。</summary>
    RunPrepared,

    /// <summary>开始执行 45 步。</summary>
    RunStarted,

    /// <summary>阶段变化。</summary>
    RunPhase,

    /// <summary>步骤开始。</summary>
    StepStarted,

    /// <summary>步骤进入等待现场确认。</summary>
    StepWaiting,

    /// <summary>步骤终态。</summary>
    StepFinished,

    /// <summary>执行器异常终止后当前步骤结果未知。</summary>
    StepUnknown,

    /// <summary>确认点创建。</summary>
    CheckpointCreated,

    /// <summary>确认点已处理或过期。</summary>
    CheckpointResolved,

    /// <summary>普通日志行；不用于推导步骤状态。</summary>
    Log,

    /// <summary>证据文件已登记。</summary>
    ArtifactCreated,

    /// <summary>清理开始。</summary>
    CleanupStarted,

    /// <summary>清理结束。</summary>
    CleanupFinished,

    /// <summary>进入待现场处理，机器占用保持。</summary>
    PendingFieldEntered,

    /// <summary>现场处理完成并通过核验。</summary>
    PendingFieldResolved,

    /// <summary>机器占用已解除。</summary>
    LockReleased,

    /// <summary>本轮结束；是本轮最后一个业务事件。</summary>
    RunFinished
}

/// <summary>清理状态，与步骤结论分开记录。</summary>
public enum CleanupStatus
{
    /// <summary>尚未进入清理。</summary>
    NotStarted,

    /// <summary>本轮没有需要清理的进程或产物。</summary>
    NotRequired,

    /// <summary>清理进行中。</summary>
    Running,

    /// <summary>清理完成。</summary>
    Succeeded,

    /// <summary>清理失败；本轮不得显示全部成功。</summary>
    Failed
}

/// <summary>确认点状态。</summary>
public enum CheckpointStatus
{
    /// <summary>等待现场确认。</summary>
    Waiting,

    /// <summary>已确认。</summary>
    Resolved,

    /// <summary>已超时，后续提交必须被拒绝。</summary>
    Expired
}
