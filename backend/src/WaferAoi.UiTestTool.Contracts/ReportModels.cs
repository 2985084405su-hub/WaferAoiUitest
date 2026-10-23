namespace WaferAoi.UiTestTool.Contracts;

/// <summary>本轮最终报告；与运行快照同源，不重新推导结论。</summary>
public sealed class RunReport
{
    /// <summary>协议版本。</summary>
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>生成时间。</summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>本轮结论。</summary>
    public RunConclusion Conclusion { get; set; }

    /// <summary>选项快照。</summary>
    public RunOptions Options { get; set; } = new();

    /// <summary>配置快照。</summary>
    public RunConfigSnapshot Config { get; set; } = new();

    /// <summary>范围说明。</summary>
    public RangeInfo Range { get; set; } = new();

    /// <summary>步骤结果。</summary>
    public List<StepState> Steps { get; set; } = new();

    /// <summary>计数。</summary>
    public StepCounts Counts { get; set; } = new();

    /// <summary>清理状态。</summary>
    public CleanupState Cleanup { get; set; } = new();

    /// <summary>待现场处理状态。</summary>
    public PendingFieldState PendingField { get; set; } = new();

    /// <summary>证据索引。</summary>
    public List<ArtifactInfo> Artifacts { get; set; } = new();

    /// <summary>是否为模拟运行。</summary>
    public bool Simulated { get; set; }

    /// <summary>事件条数。</summary>
    public long EventsCount { get; set; }

    /// <summary>补充说明。</summary>
    public string Note { get; set; } = string.Empty;
}
