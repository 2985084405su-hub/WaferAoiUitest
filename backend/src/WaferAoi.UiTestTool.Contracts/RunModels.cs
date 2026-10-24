namespace WaferAoi.UiTestTool.Contracts;

/// <summary>
/// 本轮运行选项。创建运行后固定到 runId，运行中不可修改。
/// </summary>
public sealed class RunOptions
{
    /// <summary>是否跳过版本断言；跳过时 01.02 仍保留在清单中并记为跳过。</summary>
    public bool SkipVersionAssertion { get; set; }

    /// <summary>硬件策略；两项都不选映射为 <see cref="HardwarePolicy.Strict"/>。</summary>
    public HardwarePolicy HardwarePolicy { get; set; } = HardwarePolicy.Strict;
}

/// <summary>
/// 步骤静态定义，来自源工程 MainFlowStepPlan，编号和顺序不改。
/// </summary>
public sealed class StepDefinition
{
    /// <summary>用例编号，例如 04.07。</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>步骤标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>章节号，例如 04。</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>章节标题。</summary>
    public string SectionTitle { get; set; } = string.Empty;

    /// <summary>是否涉及硬件动作或硬件状态核对。</summary>
    public bool HardwareRelated { get; set; }

    /// <summary>是否涉及版本号或编译日期断言。</summary>
    public bool VersionAssertion { get; set; }

    /// <summary>
    /// 人工参与要求：none=无；always=原用例明确要求现场物理操作；hardwarePolicyOnly=仅在人工硬件策略下等待确认。
    /// </summary>
    public string ManualRequirement { get; set; } = "none";

    /// <summary>现场操作说明；无人工要求时为空。</summary>
    public string ManualInstruction { get; set; } = string.Empty;

    /// <summary>严格执行时用于观察完成状态的关键依据，供能力表和页面详情展示。</summary>
    public string Observation { get; set; } = string.Empty;
}

/// <summary>
/// 45 步静态清单。Web 快照、执行器和文档共用同一份来源，避免三处漂移。
/// </summary>
public static class MainFlowCatalog
{
    /// <summary>本期实现的步骤数。</summary>
    public const int ImplementedStepCount = 45;

    /// <summary>完整主流程设计步数。</summary>
    public const int DesignedStepCount = 184;

    /// <summary>本期未实现的步骤数。</summary>
    public const int UnimplementedStepCount = DesignedStepCount - ImplementedStepCount;

    /// <summary>章节标题表；必须在 <see cref="Steps"/> 之前初始化，供步骤构造使用。</summary>
    private static readonly Dictionary<string, string> SectionTitles = new(StringComparer.Ordinal)
    {
        ["01"] = "启动",
        ["02"] = "登录",
        ["03"] = "放片",
        ["04"] = "新建配方 A",
        ["05"] = "晶圆信息",
        ["06"] = "建档上片",
        ["07"] = "切割道两点角度校正"
    };

    /// <summary>
    /// 按文档顺序排列的 45 步。标题与源工程 MainFlowStepPlan 完全一致。
    /// </summary>
    public static readonly IReadOnlyList<StepDefinition> Steps = new List<StepDefinition>
    {
        Step("01.01", "启动并检查单实例及硬件状态登录窗口", "01",
            hardwareRelated: true,
            observation: "登录窗口出现、单实例检查通过、硬件状态可在启动流程中观察"),
        Step("01.02", "登录后核对软件版本和编译日期并截图", "01",
            versionAssertion: true,
            observation: "页面显示的版本号与编译日期与本轮安装记录一致"),
        Step("02.01", "选择 admin 并登录", "02",
            observation: "选中 admin 并成功进入主界面"),
        Step("02.02", "处理启动初始化及回零提示", "02",
            hardwareRelated: true,
            manualRequirement: "hardwarePolicyOnly",
            manualInstruction: "按设备启动规程处理“全部轴回零”提示，等待回零完成，并核对相机、轴、EFEM、光源与报警区；完成后在网页确认。",
            observation: "回零提示被处理且回零完成，阻断报警有明确状态"),
        Step("03.01", "放片及工位空片检查，记录本轮开始时间", "03",
            hardwareRelated: true,
            manualRequirement: "always",
            manualInstruction: "按现场规程在 Loadport1 槽1 放置晶圆并完成装盒，核对工位无遗留晶圆；完成后在网页确认。",
            observation: "现场放片完成确认；工位空片状态可观察"),
        Step("03.02", "任务晶圆分布页选择 Loadport1", "03",
            hardwareRelated: true,
            manualRequirement: "always",
            manualInstruction: "核对晶圆分布页 Loadport1 槽1 的识别状态与实物一致；槽图未更新时先完成机台正常装盒扫描，不改数据库片位。",
            observation: "任务晶圆分布页显示 Loadport1 且选择回读一致"),
        Step("04.01", "打开新建配方窗口", "04",
            hardwareRelated: true,
            observation: "新建配方窗口打开；平台无片/吸附提示按实际条件处理"),
        Step("04.02", "文件类型选配方文件并回读", "04",
            observation: "文件类型回读为“配方文件”"),
        Step("04.03", "输入带秒级时间戳的配方名称并回读", "04",
            observation: "名称含本轮秒级时间戳且回读一致"),
        Step("04.04", "创建位置选根目录并回读", "04",
            observation: "创建位置回读为“根目录”"),
        Step("04.05", "选择常规建档模式并回读", "04",
            observation: "建档模式回读为“常规建档模式”"),
        Step("04.06", "选择 300 和 Notch 并回读", "04",
            observation: "晶圆尺寸 300、缺口类型 Notch 回读一致"),
        Step("04.07", "确定新建并检查配方树、当前配方及落盘", "04",
            observation: "配方树出现新配方、当前配方为 A，且目录落盘"),
        Step("05.01", "打开晶圆信息页并选择正面2D检测", "05",
            observation: "晶圆信息页打开且检测类型回读为正面2D检测"),
        Step("05.02", "输入产品ID和工序ID并回读", "05",
            observation: "产品ID、工序ID回读一致"),
        Step("05.03", "取消仿真模式", "05",
            observation: "仿真模式处于取消状态"),
        Step("05.04", "选择注水和清洗次数1", "05",
            observation: "介质模式为注水、清洗次数为 1"),
        Step("05.05", "输入注水冲洗时间并回读", "05",
            observation: "注水冲洗时间回读为 30000"),
        Step("05.06", "输入注水后吸水时间并回读", "05",
            observation: "注水后吸水时间回读为 30000"),
        Step("05.07", "输入吸附晶圆后的吸水时间并回读", "05",
            observation: "吸附晶圆后的吸水时间回读为 5000"),
        Step("05.08", "输入吸附晶圆的真空时间并回读", "05",
            observation: "吸附晶圆的真空时间回读为 60000"),
        Step("05.09", "处理风刀字段并保存配方A", "05",
            observation: "风刀字段处理完成且配方 A 保存成功"),
        Step("06.01", "任务晶圆分布页选择Loadport1层1", "06",
            hardwareRelated: true,
            manualRequirement: "always",
            manualInstruction: "核对 Loadport1 层1 槽1 的识别状态与实物一致：固定测试片在槽1；槽图未更新时先完成正常装盒扫描。",
            observation: "Loadport1 层1 选择回读一致，槽位与实物一致"),
        Step("06.02", "选择关闭读码和对准角0", "06",
            observation: "读码方式为关闭读码、对准角为 0"),
        Step("06.03", "确认一键上片按钮并交现场执行上片", "06",
            hardwareRelated: true,
            manualRequirement: "hardwarePolicyOnly",
            manualInstruction: "点击一键上片后，按原用例要求完成现场确认，再在网页确认现场已执行上片。",
            observation: "一键上片被触发，符合原用例条件的提示已处理"),
        Step("06.04", "等待上片完成并核对平台及来源槽位", "06",
            hardwareRelated: true,
            manualRequirement: "hardwarePolicyOnly",
            manualInstruction: "等待上片完成，核对实片在运动平台、原 Loadport1 槽1 已空、机械手无遗留片、报警区无搬片报警；完成后在网页确认。",
            observation: "平台有片、来源槽位为 Loadport1 层1、机械手状态及报警可核对"),
        Step("06.05", "点击配方确认当前配方仍为A", "06",
            observation: "当前配方仍为 A"),

        // 07 切割道两点角度校正（07.01—07.18）
        Step("07.01", "打开晶圆角度校正页并核对点位列表与四个算法参数", "07",
            observation: "当前编辑配方为 A；点位列表、“增加”“采图调试”和低阈值等四个参数均可见"),
        Step("07.02", "选择 InspectCamera 相机", "07",
            hardwareRelated: true,
            observation: "相机回读为 InspectCamera，运动与光学操作无硬件错误"),
        Step("07.03", "倍率选择 5X 并点击设置", "07",
            hardwareRelated: true,
            observation: "倍率为 5X，设置无失败或未标定提示"),
        Step("07.04", "明场照明 300 并设置，关闭红外与可见暗场", "07",
            hardwareRelated: true,
            observation: "明场勾选且值回读 300；红外（及可见暗场）未勾选并已关闭"),
        Step("07.05", "核对并设置可见的曝光、硬件增益与逻辑增益", "07",
            hardwareRelated: true,
            observation: "可见字段回读 1000us、1/1；未显示字段记为未显示，不记为已设置"),
        Step("07.06", "选择新转正方式和切割后", "07",
            observation: "新转正选中、旧转正未选；切割后选中、切割前未选"),
        Step("07.07", "低阈值输入 50 并回读", "07",
            observation: "低阈值回读 50（灰度 0—255 内）"),
        Step("07.08", "Die高度输入 500 并回读", "07",
            observation: "Die高度回读 500px"),
        Step("07.09", "黑白切割道填 1、粗校正系数填 1 并回读", "07",
            observation: "两框回读 1"),
        Step("07.10", "连续采集并在寸动中取景到水平黑切割道", "07",
            hardwareRelated: true,
            manualRequirement: "always",
            manualInstruction: "图像持续刷新中：用寸动把视野移到片中央附近一条水平黑切割道，确认画面能看见切割道及相邻 Die 边缘、所选行无墨点、晶粒正常，且向 X 正方向 20mm 范围都在片内、轴未接近限位。完成后在网页确认。",
            observation: "连续采集运行中；画面可见切割道与相邻 Die 边缘；取景位置与 20mm 范围在片内"),
        Step("07.11", "Z 调焦后停止连续采集并采单张", "07",
            hardwareRelated: true,
            manualRequirement: "always",
            manualInstruction: "步距已改为 0.010：用标为 Z 的上下方向按钮逐步调焦，直到切割道和 Die 边缘清晰、图像非全黑/全白；调好后不要动 Theta，在网页确认。脚本随后恢复步距 0.100、停止连续采集并采单张。",
            observation: "图像清晰非全黑/全白；连续采集已停止，步距回读 0.100；记录当前 Z/Theta"),
        Step("07.12", "核对点位列表为空并用“增加”采集第一点", "07",
            hardwareRelated: true,
            observation: "列表恰有 1 行，X1/Y1 与当前轴位四舍五入到 4 位小数一致"),
        Step("07.13", "步距 1.000 点击 X 左箭头 20 次并核验位移", "07",
            hardwareRelated: true,
            manualRequirement: "hardwarePolicyOnly",
            manualInstruction: "在寸动“步距”填 1.000 并离焦，逐次点击 X 轴左侧方向按钮共 20 次（每次等运动停稳再点下一次），不要点 Y 方向按钮；确认 X 读数相对 X1 增加约 20.0000mm、Y 保持不变。完成后在网页确认。",
            observation: "X 实际读数相对 X1 增加约 20mm，Y 保持 Y1，无限位或运动错误"),
        Step("07.14", "采单张并采集第二点，核对同 Y 与 20mm 间距", "07",
            hardwareRelated: true,
            observation: "列表恰有 2 行；X2 约为 X1＋20mm，Y2＝Y1，第二张图清晰，步距恢复 0.100"),
        Step("07.15", "保存角度校正并确认无点位过少提示", "07",
            hardwareRelated: true,
            observation: "无“点位过少”或“保存失败”提示；列表仍为 2 点，四参数仍 50/500/1/1"),
        Step("07.16", "移动回第一点并采图调试", "07",
            hardwareRelated: true,
            observation: "轴回到第一点 X1/Y1 及已保存焦高；出现非空调试图，无调试算法报错"),
        Step("07.17", "运行切割道角度校正并确认运行成功提示", "07",
            hardwareRelated: true,
            manualRequirement: "hardwarePolicyOnly",
            manualInstruction: "点击页面底部公共“运行”，等待弹窗；确认提示为“运行成功!”后点击“确定”。没有粗/精校准失败、飞拍超时、限位、标定或相机错误。完成后在网页确认。",
            observation: "被测程序返回“运行成功!”，无校准失败、飞拍超时、限位、标定或相机错误"),
        Step("07.18", "读取角度偏移与纠正后角度并留证", "07",
            hardwareRelated: true,
            observation: "角度偏移、纠正后角度均为有限数值（允许为 0），保持算法输出，未手填")
    };

    /// <summary>按编号索引的步骤定义。</summary>
    private static readonly Dictionary<string, StepDefinition> ById =
        Steps.ToDictionary(step => step.StepId, StringComparer.Ordinal);

    /// <summary>按编号取得步骤定义。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <returns>步骤定义；编号不存在时抛出异常，避免执行未登记的步骤。</returns>
    public static StepDefinition Get(string stepId) =>
        ById.TryGetValue(stepId, out StepDefinition? step)
            ? step
            : throw new ArgumentException($"步骤 {stepId} 不在本期 45 步登记表中。", nameof(stepId));

    /// <summary>判断编号是否属于本期范围。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <returns>已登记时为 true。</returns>
    public static bool Contains(string stepId) => ById.ContainsKey(stepId);

    /// <summary>为步骤补齐章节标题。</summary>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="title">步骤标题。</param>
    /// <param name="section">章节号。</param>
    /// <param name="hardwareRelated">是否涉及硬件。</param>
    /// <param name="versionAssertion">是否涉及版本断言。</param>
    /// <param name="manualRequirement">人工参与要求。</param>
    /// <param name="manualInstruction">现场说明。</param>
    /// <param name="observation">完成状态观察依据。</param>
    /// <returns>步骤定义。</returns>
    private static StepDefinition Step(
        string stepId,
        string title,
        string section,
        bool hardwareRelated = false,
        bool versionAssertion = false,
        string manualRequirement = "none",
        string manualInstruction = "",
        string observation = "") =>
        new()
        {
            StepId = stepId,
            Title = title,
            Section = section,
            SectionTitle = SectionTitles[section],
            HardwareRelated = hardwareRelated,
            VersionAssertion = versionAssertion,
            ManualRequirement = manualRequirement,
            ManualInstruction = manualInstruction,
            Observation = observation
        };
}

/// <summary>
/// 单步运行状态快照。
/// </summary>
public sealed class StepState
{
    /// <summary>步骤编号。</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>步骤标题。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>章节号。</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>章节标题。</summary>
    public string SectionTitle { get; set; } = string.Empty;

    /// <summary>当前状态。</summary>
    public StepStatus Status { get; set; } = StepStatus.NotRun;

    /// <summary>跳过、阻塞、失败或人工确认原因。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>耗时毫秒；执行中为从开始到最近事件的时间。</summary>
    public long? DurationMs { get; set; }

    /// <summary>是否包含人工确认。</summary>
    public bool Manual { get; set; }

    /// <summary>人工参与要求，与静态定义一致。</summary>
    public string ManualRequirement { get; set; } = "none";

    /// <summary>是否涉及硬件。</summary>
    public bool HardwareRelated { get; set; }

    /// <summary>硬件部分是否被跳过。</summary>
    public bool HardwareSkipped { get; set; }

    /// <summary>是否涉及版本断言。</summary>
    public bool VersionAssertion { get; set; }

    /// <summary>已登记的证据 ID。</summary>
    public List<string> EvidenceIds { get; set; } = new();

    /// <summary>补充说明，按发生顺序保存。</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// 运行计数。每个步骤只计一次；分母固定 45。
/// </summary>
public sealed class StepCounts
{
    /// <summary>成功（含人工确认成功）。</summary>
    public int Passed { get; set; }

    /// <summary>其中的人工确认成功数，是 <see cref="Passed"/> 的子集。</summary>
    public int ManualPassed { get; set; }

    /// <summary>跳过（整步硬件、部分硬件、明确跳过）。</summary>
    public int Skipped { get; set; }

    /// <summary>阻塞。</summary>
    public int Blocked { get; set; }

    /// <summary>失败。</summary>
    public int Failed { get; set; }

    /// <summary>未执行。</summary>
    public int NotRun { get; set; }

    /// <summary>等待现场确认；不计入四类结果。</summary>
    public int Waiting { get; set; }

    /// <summary>中断且结果未知；不计入四类结果。</summary>
    public int Unknown { get; set; }

    /// <summary>步骤总数，固定为本期 45 步。</summary>
    public int Total { get; set; } = MainFlowCatalog.ImplementedStepCount;
}

/// <summary>清理状态快照。</summary>
public sealed class CleanupState
{
    /// <summary>清理状态。</summary>
    public CleanupStatus Status { get; set; } = CleanupStatus.NotStarted;

    /// <summary>开始时间。</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>清理结论或失败原因。</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>待现场处理状态；跨宿主重启保留。</summary>
public sealed class PendingFieldState
{
    /// <summary>是否正在占用机器等待现场处理。</summary>
    public bool Active { get; set; }

    /// <summary>进入待处理的时间。</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>进入待处理的原因。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>本轮被测程序 PID；身份不明时为 0。</summary>
    public int ProcessId { get; set; }

    /// <summary>被测程序启动时间，用于核对 PID 未被复用。</summary>
    public DateTimeOffset? ProcessStartedAt { get; set; }

    /// <summary>现场提交的说明。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>是否已核验原进程退出。</summary>
    public bool ProcessExitedVerified { get; set; }

    /// <summary>现场是否明确确认设备状态已处理。</summary>
    public bool DeviceStateVerified { get; set; }

    /// <summary>仍需完成的动作。</summary>
    public string RequiredAction { get; set; } =
        "按现场规程处理设备并关闭本轮被测程序，然后在网页提交现场处理完成；禁止强制解锁。";
}

/// <summary>执行器状态。</summary>
public sealed class ExecutorState
{
    /// <summary>执行器 PID。</summary>
    public int ProcessId { get; set; }

    /// <summary>执行器是否仍存活。</summary>
    public bool Alive { get; set; }

    /// <summary>执行器启动时间。</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>最近事件时间。</summary>
    public DateTimeOffset? LastEventAt { get; set; }

    /// <summary>最近事件序号。</summary>
    public long LastSequence { get; set; }

    /// <summary>退出码；未知时为 null。</summary>
    public int? ExitCode { get; set; }
}

/// <summary>确认点快照。</summary>
public sealed class CheckpointState
{
    /// <summary>确认点唯一标识。</summary>
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>所属步骤。</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>现场操作说明。</summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>创建时间，也是等待起点。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>超时时间。</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>状态。</summary>
    public CheckpointStatus Status { get; set; } = CheckpointStatus.Waiting;

    /// <summary>现场结果。</summary>
    public CheckpointResult? Result { get; set; }

    /// <summary>现场备注或原因。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>处理时间。</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>处理该确认点的请求标识，用于幂等与追溯。</summary>
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>已登记证据。</summary>
public sealed class ArtifactInfo
{
    /// <summary>证据 ID，由执行器生成并在本轮内唯一。</summary>
    public string ArtifactId { get; set; } = string.Empty;

    /// <summary>证据文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>类别：log、screenshot、tree、report、other。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>MIME 类型。</summary>
    public string MimeType { get; set; } = "application/octet-stream";

    /// <summary>字节数。</summary>
    public long Size { get; set; }

    /// <summary>登记时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>关联步骤；为空表示本轮级证据。</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>文件是否仍然存在；现场移走或删除后为 false。</summary>
    public bool Available { get; set; } = true;
}

/// <summary>本轮范围说明。</summary>
public sealed class RangeInfo
{
    /// <summary>本期实现步数。</summary>
    public int Implemented { get; set; } = MainFlowCatalog.ImplementedStepCount;

    /// <summary>完整设计步数。</summary>
    public int Designed { get; set; } = MainFlowCatalog.DesignedStepCount;

    /// <summary>其余未实现步数。</summary>
    public int Unimplemented { get; set; } = MainFlowCatalog.UnimplementedStepCount;
}

/// <summary>运行快照；SSE 首订阅和刷新恢复都以它为准。</summary>
public sealed class RunSnapshot
{
    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最近更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>当前阶段。</summary>
    public RunPhase Phase { get; set; } = RunPhase.Created;

    /// <summary>本轮结论。</summary>
    public RunConclusion Conclusion { get; set; } = RunConclusion.Pending;

    /// <summary>本轮选项快照。</summary>
    public RunOptions Options { get; set; } = new();

    /// <summary>不含密码的配置快照。</summary>
    public RunConfigSnapshot Config { get; set; } = new();

    /// <summary>本期范围说明。</summary>
    public RangeInfo Range { get; set; } = new();

    /// <summary>按文档顺序排列的 45 步状态。</summary>
    public List<StepState> Steps { get; set; } = new();

    /// <summary>状态计数。</summary>
    public StepCounts Counts { get; set; } = new();

    /// <summary>清理状态。</summary>
    public CleanupState Cleanup { get; set; } = new();

    /// <summary>待现场处理状态。</summary>
    public PendingFieldState PendingField { get; set; } = new();

    /// <summary>执行器状态。</summary>
    public ExecutorState Executor { get; set; } = new();

    /// <summary>当前或最近确认点。</summary>
    public CheckpointState? Checkpoint { get; set; }

    /// <summary>已登记证据。</summary>
    public List<ArtifactInfo> Artifacts { get; set; } = new();

    /// <summary>最后已应用的事件序号。</summary>
    public long LastSequence { get; set; }

    /// <summary>是否为契约验证用的模拟运行；模拟结果不得当作实机通过。</summary>
    public bool Simulated { get; set; }

    /// <summary>最近一条日志。</summary>
    public string LastMessage { get; set; } = string.Empty;
}

/// <summary>历史列表条目。</summary>
public sealed class RunSummary
{
    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>阶段。</summary>
    public RunPhase Phase { get; set; }

    /// <summary>结论。</summary>
    public RunConclusion Conclusion { get; set; }

    /// <summary>硬件策略。</summary>
    public HardwarePolicy HardwarePolicy { get; set; }

    /// <summary>是否跳过版本断言。</summary>
    public bool SkipVersionAssertion { get; set; }

    /// <summary>清理状态。</summary>
    public CleanupStatus CleanupStatus { get; set; }

    /// <summary>是否仍待现场处理。</summary>
    public bool PendingFieldActive { get; set; }

    /// <summary>是否模拟运行。</summary>
    public bool Simulated { get; set; }

    /// <summary>计数。</summary>
    public StepCounts Counts { get; set; } = new();
}

/// <summary>
/// 结构化事件。持久化后推送，sequence 在同一 runId 内严格递增。
/// </summary>
public sealed class RunEvent
{
    /// <summary>协议版本，当前为 1。</summary>
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>递增序号，从 1 开始。</summary>
    public long Sequence { get; set; }

    /// <summary>带时区的事件时间。</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>事件类型。</summary>
    public EventType Type { get; set; }

    /// <summary>步骤编号；非步骤事件为空。</summary>
    public string? StepId { get; set; }

    /// <summary>阶段；未变化时可为空。</summary>
    public RunPhase? Phase { get; set; }

    /// <summary>步骤状态。</summary>
    public StepStatus? Status { get; set; }

    /// <summary>跳过、阻塞、失败、等待或中断原因。</summary>
    public string? Reason { get; set; }

    /// <summary>确认点标识。</summary>
    public string? CheckpointId { get; set; }

    /// <summary>证据标识。</summary>
    public string? ArtifactId { get; set; }

    /// <summary>证据文件名。</summary>
    public string? ArtifactName { get; set; }

    /// <summary>证据类别。</summary>
    public string? ArtifactKind { get; set; }

    /// <summary>证据 MIME 类型。</summary>
    public string? MimeType { get; set; }

    /// <summary>证据字节数。</summary>
    public long? ArtifactSize { get; set; }

    /// <summary>日志文本或补充说明。</summary>
    public string? Message { get; set; }

    /// <summary>附加键值；只允许标量文本，避免协议流里出现任意结构。</summary>
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>运行配置快照；密码永远不进入该对象。</summary>
public sealed class RunConfigSnapshot
{
    /// <summary>被测程序 EXE 绝对路径。</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>配方目录。</summary>
    public string RecipeDir { get; set; } = string.Empty;

    /// <summary>证据根目录。</summary>
    public string EvidenceDir { get; set; } = string.Empty;

    /// <summary>预期版本号。</summary>
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>预期编译日期，保存为 yyyy-MM-dd HH:mm。</summary>
    public string BuildDate { get; set; } = string.Empty;

    /// <summary>模板配方名称。</summary>
    public string TemplateRecipeName { get; set; } = "1";

    /// <summary>固定登录账号。</summary>
    public string UserName { get; set; } = "admin";

    /// <summary>生产配置目录。</summary>
    public string ConfigurationRoot { get; set; } = string.Empty;

    /// <summary>密码环境变量名；只保存名称，不保存值。</summary>
    public string PasswordEnvironmentVariable { get; set; } = "WAFER_AOI_UI_PASSWORD";

    /// <summary>是否已配置密码；只表示存在性。</summary>
    public bool PasswordConfigured { get; set; }
}

/// <summary>
/// 执行器输入清单。Web 创建后写入本轮目录，执行器只读取，不接受宿主环境变量改变已创建运行。
/// </summary>
public sealed class RunManifest
{
    /// <summary>协议版本。</summary>
    public int ProtocolVersion { get; set; } = 1;

    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>本轮目录绝对路径。</summary>
    public string RunDir { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>选项快照。</summary>
    public RunOptions Options { get; set; } = new();

    /// <summary>配置快照。</summary>
    public RunConfigSnapshot Config { get; set; } = new();

    /// <summary>27 步定义。</summary>
    public List<StepDefinition> Steps { get; set; } = new();

    /// <summary>确认等待超时秒数。</summary>
    public int CheckpointTimeoutSeconds { get; set; } = 1800;

    /// <summary>是否模拟运行。</summary>
    public bool Simulation { get; set; }

    /// <summary>模拟场景：strict、skip-version、skip-hardware、manual、fail、pending-field。</summary>
    public string? Scenario { get; set; }
}

/// <summary>创建运行请求。</summary>
public sealed class CreateRunRequest
{
    /// <summary>客户端幂等标识；同一标识重复提交返回同一运行。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>本轮选项；为空时使用配置中的默认选项。</summary>
    public RunOptions? Options { get; set; }

    /// <summary>可选备注。</summary>
    public string? Note { get; set; }

    /// <summary>
    /// 兼容页面直传的“跳过硬件步骤”布尔；与 <see cref="ManualHardwareSteps"/> 同时为 true 时拒绝创建。
    /// </summary>
    public bool? SkipHardwareSteps { get; set; }

    /// <summary>兼容页面直传的“人工执行硬件步骤”布尔。</summary>
    public bool? ManualHardwareSteps { get; set; }
}

/// <summary>创建运行响应。</summary>
public sealed class CreateRunResponse
{
    /// <summary>是否新建；false 表示幂等重放。</summary>
    public bool Created { get; set; }

    /// <summary>运行快照。</summary>
    public RunSnapshot Run { get; set; } = new();
}

/// <summary>确认请求。</summary>
public sealed class ConfirmCheckpointRequest
{
    /// <summary>请求标识；同一确认点内重复提交幂等。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>现场结果。</summary>
    public CheckpointResult Result { get; set; }

    /// <summary>阻塞或失败时必须填写的原因。</summary>
    public string? Reason { get; set; }

    /// <summary>完成时的现场备注。</summary>
    public string? Note { get; set; }
}

/// <summary>确认响应。</summary>
public sealed class ConfirmCheckpointResponse
{
    /// <summary>是否已接受。</summary>
    public bool Accepted { get; set; }

    /// <summary>是否为同一请求的幂等重放。</summary>
    public bool Replayed { get; set; }

    /// <summary>执行器是否已回执。</summary>
    public bool Applied { get; set; }

    /// <summary>确认点标识。</summary>
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>步骤编号。</summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>确认点状态。</summary>
    public CheckpointStatus Status { get; set; }

    /// <summary>可展示说明。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>现场处理完成请求。</summary>
public sealed class FieldResolutionRequest
{
    /// <summary>请求标识，用于幂等。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>现场确认本轮被测程序已退出。</summary>
    public bool ProcessExitedConfirmed { get; set; }

    /// <summary>现场确认设备状态已按规程处理。</summary>
    public bool DeviceStateConfirmed { get; set; }

    /// <summary>现场处理说明。</summary>
    public string? Note { get; set; }
}

/// <summary>现场处理核验项。</summary>
public sealed class FieldVerification
{
    /// <summary>核验项标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>核验项名称。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>是否通过。</summary>
    public bool Passed { get; set; }

    /// <summary>说明。</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>现场处理响应。</summary>
public sealed class FieldResolutionResponse
{
    /// <summary>是否已解除占用。</summary>
    public bool Released { get; set; }

    /// <summary>可展示说明。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>核验明细。</summary>
    public List<FieldVerification> Verifications { get; set; } = new();
}

/// <summary>API 错误体。</summary>
public sealed class ApiError
{
    /// <summary>机器可读错误码。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>可展示消息。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>相关运行；机器忙时返回现有运行。</summary>
    public RunSnapshot? CurrentRun { get; set; }

    /// <summary>字段级校验错误。</summary>
    public List<ValidationIssue>? Issues { get; set; }
}

/// <summary>字段级校验错误。</summary>
public sealed class ValidationIssue
{
    /// <summary>字段名，与前端表单字段一致。</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>机器可读错误码：required、invalid_format、not_found、not_executable、not_writable。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>可展示中文说明。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>启动前检查项；不作为用例步骤通过。</summary>
public sealed class PreparationCheck
{
    /// <summary>检查标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>检查名称。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>结果：ok、warning、failed。</summary>
    public string Status { get; set; } = "ok";

    /// <summary>明细。</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>配置校验结果。</summary>
public sealed class ConfigValidationResult
{
    /// <summary>是否允许开始执行。</summary>
    public bool Valid { get; set; }

    /// <summary>校验时间。</summary>
    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>字段级错误。</summary>
    public List<ValidationIssue> Issues { get; set; } = new();

    /// <summary>准备检查项。</summary>
    public List<PreparationCheck> Checks { get; set; } = new();
}
