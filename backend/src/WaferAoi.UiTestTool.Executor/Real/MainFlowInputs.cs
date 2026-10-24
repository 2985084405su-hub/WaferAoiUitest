namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 主流程固定输入，逐项对应用例文档“本条固定输入”表。
/// 数值与源工程 Tests/MainFlow/Infrastructure/MainFlowInputs.cs 一致，不在此处调整算法参数。
/// </summary>
internal static class MainFlowInputs
{
    /// <summary>04.04 要求的配方根目录。</summary>
    public const string RecipeRoot = @"D:/GlobalConfig/Recipe";

    /// <summary>新建配方 A 的名称前缀；每轮追加秒级时间戳。</summary>
    public const string RecipeAPrefix = "REG_MAIN_FLOW_A";

    /// <summary>建档与正式任务固定端口。</summary>
    public const string Loadport = "Loadport1";

    /// <summary>06.01 上片来源层号。</summary>
    public const string LoadportLayer = "1";

    /// <summary>新建属性：文件类型。</summary>
    public const string RecipeFileType = "配方文件";

    /// <summary>新建属性：创建位置。</summary>
    public const string CreateLocation = "根目录";

    /// <summary>新建属性：建档模式。</summary>
    public const string SetupMode = "常规建档模式";

    /// <summary>新建属性：晶圆尺寸。</summary>
    public const string OuterDiameter = "300";

    /// <summary>新建属性：缺口类型。</summary>
    public const string FiducialType = "Notch";

    /// <summary>05.01 检测类型：正面2D检测。</summary>
    public static readonly UiSelectionValue InspectType = new("正面2D检测", "DefectInspect2D");

    /// <summary>05.02 固定产品 ID。</summary>
    public const string DeviceId = "DEV_MAIN_FLOW";

    /// <summary>05.02 固定工序 ID。</summary>
    public const string StepId = "STEP_001";

    /// <summary>05.04 介质模式：注水。</summary>
    public static readonly UiSelectionValue WaterInjectionMediumMode = new("注水", "Water");

    /// <summary>05.04 固定清洗次数。</summary>
    public const int WashCount = 1;

    /// <summary>05.05 注水冲洗时间（ms）。</summary>
    public const int InjectionWashTime = 30000;

    /// <summary>05.06 注水后吸水时间（ms）。</summary>
    public const int PostInjectionSoakTime = 30000;

    /// <summary>05.07 吸附晶圆后的吸水时间（ms）。</summary>
    public const int WaferSoakTime = 5000;

    /// <summary>05.08 吸附晶圆的真空时间（ms）。</summary>
    public const int VacuumTime = 60000;

    /// <summary>06.02 读码方式：关闭读码。</summary>
    public const string ReadCodeMode = "关闭读码";

    /// <summary>06.02 对准角。</summary>
    public const string AlignmentAngle = "0";

    // ---- 07 切割道两点角度校正（见用例 07.01—07.18） ----

    /// <summary>07.02 相机。</summary>
    public const string AngleCamera = "InspectCamera";

    /// <summary>07.03 倍率。</summary>
    public const string AngleMagnification = "5X";

    /// <summary>07.04 明场照明值。</summary>
    public const string BrightFieldValue = "300";

    /// <summary>07.05 曝光时间（us）。</summary>
    public const string ExposureMicroseconds = "1000";

    /// <summary>07.05 硬件增益与逻辑增益。</summary>
    public const string Gain = "1";

    /// <summary>07.07 低阈值（灰度 0—255）。</summary>
    public const string LowThreshold = "50";

    /// <summary>07.08 Die 高度（px）。</summary>
    public const string DieHeight = "500";

    /// <summary>07.09 黑白切割道：黑填1。</summary>
    public const string BlackWhiteStreet = "1";

    /// <summary>07.09 粗校正系数（不大于 1）。</summary>
    public const string CoarseCorrection = "1";

    /// <summary>07.10 / 07.14 取景与恢复的 XY 步距（mm）。</summary>
    public const string JogStepXy = "0.100";

    /// <summary>07.11 / 07.13 使用的 Z 调焦与 X 两点间距步距（mm）。</summary>
    public const string JogStepFine = "0.010";

    /// <summary>07.13 两点间距步距（mm），共 20 次。</summary>
    public const string JogStepGap = "1.000";

    /// <summary>07.13 固定点击次数。</summary>
    public const int JogGapClicks = 20;

    /// <summary>07.13 期望 X 位移（mm）与允许误差（mm）。</summary>
    public const double ExpectedDeltaX = 20.0000;

    /// <summary>07.13 X 位移允许误差（mm）：按位置显示精度核对。</summary>
    public const double DeltaXTolerance = 0.02;

    /// <summary>07.17 运行成功提示关键字。</summary>
    public const string RunSuccessKeyword = "运行成功";

    /// <summary>
    /// 用户获准的“组合框 ID → 精确选项文字”定位例外；只允许这些组合框按文字选择。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ApprovedSelections = new Dictionary<string, string>
    {
        ["cmbLP"] = Loadport,
        ["cmbLPLayer"] = LoadportLayer,
        ["cmbEnableScan"] = ReadCodeMode,
        ["cmbAngle"] = AlignmentAngle,
        ["RecipeNew.Type"] = RecipeFileType,
        ["RecipeNew.CreateLocation"] = CreateLocation,
        ["RecipeNew.SetupMode"] = SetupMode,
        ["RecipeNew.OuterDiameter"] = OuterDiameter,
        ["RecipeNew.FiducialType"] = FiducialType
    };
}
