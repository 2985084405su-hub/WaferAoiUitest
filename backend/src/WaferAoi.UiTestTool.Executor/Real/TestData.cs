namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 一个下拉选项的界面显示名称与配方 XML 持久化值。
/// 迁移自源工程 TestData/WaferInformationConfiguration.cs。
/// </summary>
internal sealed class UiSelectionValue
{
    /// <summary>创建下拉选项值。</summary>
    /// <param name="displayName">界面显示名称。</param>
    /// <param name="persistedValue">配方 XML 中的持久化值。</param>
    public UiSelectionValue(string displayName, string persistedValue)
    {
        DisplayName = displayName;
        PersistedValue = persistedValue;
    }

    /// <summary>界面显示名称。</summary>
    public string DisplayName { get; }

    /// <summary>配方 XML 持久化值。</summary>
    public string PersistedValue { get; }
}

/// <summary>
/// 三条吹干参数中的时间字段集合，供 05.05—05.08 与落盘核对共用。
/// </summary>
internal sealed class WaferBlowDryTimes
{
    /// <summary>注水冲洗时间（ms）。</summary>
    public int BlowDryingInjectWaterDelay { get; init; }

    /// <summary>注水后吸水时间（ms）。</summary>
    public int BlowDryingVacWaterDelay { get; init; }

    /// <summary>吸附晶圆后的吸水时间（ms）。</summary>
    public int BlowDryingDelay { get; init; }

    /// <summary>吸附晶圆的真空时间（ms）。</summary>
    public int VacWaferTime { get; init; }
}

/// <summary>
/// 本轮生产界面新建出来的配方产物（文件 + 同名资源目录）。
/// </summary>
internal sealed class TestRecipe
{
    /// <summary>不含 .rcp 的配方名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>配方文件绝对路径。</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>同名资源目录绝对路径。</summary>
    public string ResourceDirectory { get; init; } = string.Empty;
}
