using System.Globalization;
using System.Xml.Linq;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 主流程 05：在新建配方 A 的晶圆信息页写入固定输入并保存，随后核对页面、当前配方及磁盘。
/// 配方自动打开时产品默认选中“晶圆信息”，因此不操作没有业务 ID 的步骤导航项。
/// </summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>按文档顺序执行 05.01—05.09。</summary>
    private void RunWaferInformationSection()
    {
        Step("05.01", SelectInspectionType);
        if (_stoppedStatus != null) return;
        Step("05.02", _ => SetWaferIdentifiers());
        if (_stoppedStatus != null) return;
        Step("05.03", _ => DisableSimulationMode());
        if (_stoppedStatus != null) return;
        Step("05.04", _ => SelectWaterInjectionAndWashCount());
        if (_stoppedStatus != null) return;
        Step("05.05", _ => SetTimeAndAssertNoMessage(MainFlowInputs.InjectionWashTime, _waferInformation.SetInjectionWashTime));
        if (_stoppedStatus != null) return;
        Step("05.06", _ => SetTimeAndAssertNoMessage(MainFlowInputs.PostInjectionSoakTime, _waferInformation.SetPostInjectionSoakTime));
        if (_stoppedStatus != null) return;
        Step("05.07", _ => SetTimeAndAssertNoMessage(MainFlowInputs.WaferSoakTime, _waferInformation.SetWaferSoakTime));
        if (_stoppedStatus != null) return;
        Step("05.08", _ => SetTimeAndAssertNoMessage(MainFlowInputs.VacuumTime, _waferInformation.SetVacuumTime));
        if (_stoppedStatus != null) return;
        Step("05.09", SaveWaferInformation);
    }

    /// <summary>05.01 等待页面就绪，选择正面2D检测并回读业务键与显示文字。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SelectInspectionType(StepContext context)
    {
        _waferInformation = new WaferInformationPage(_main, _settings.UiTimeout);
        _waferInformation.WaitUntilLoaded();
        context.Note("配方自动打开时默认选中第一项“晶圆信息”，未操作无业务 ID 的步骤导航项。");
        UiSelectionValue actual = _waferInformation.SelectInspectionType(MainFlowInputs.InspectType);
        Verify.Equal(MainFlowInputs.InspectType.PersistedValue, actual.PersistedValue, "检测类型业务键与用例固定值不一致。");
        Verify.Equal(MainFlowInputs.InspectType.DisplayName, actual.DisplayName, "检测类型显示文字与用例固定值不一致。");
        AssertNoMessageBox();
    }

    /// <summary>05.02 输入产品 ID 与工序 ID，分别失焦后回读完整文本。</summary>
    private void SetWaferIdentifiers()
    {
        _waferInformation.SetDeviceId(MainFlowInputs.DeviceId);
        _waferInformation.SetStepId(MainFlowInputs.StepId);
        _waferInformation.CommitInput();
        Verify.Equal(MainFlowInputs.DeviceId, _waferInformation.ReadDeviceId(), "产品ID回读不一致。");
        Verify.Equal(MainFlowInputs.StepId, _waferInformation.ReadStepId(), "工序ID回读不一致。");
        AssertNoMessageBox();
    }

    /// <summary>05.03 取消仿真模式并回读。</summary>
    private void DisableSimulationMode()
    {
        _waferInformation.EnsureSimulationMode(false);
        Verify.That(!_waferInformation.ReadSimulationMode(), "仿真模式应保持未勾选。");
        AssertNoMessageBox();
    }

    /// <summary>05.04 选择注水和清洗次数 1；缺少该选项时按环境不匹配阻塞。</summary>
    private void SelectWaterInjectionAndWashCount()
    {
        _waferInformation.AssertBlowDryingAvailable();
        UiSelectionValue medium = _waferInformation.SelectWaterInjectionMedium(MainFlowInputs.WaterInjectionMediumMode);
        Verify.Equal(MainFlowInputs.WaterInjectionMediumMode.PersistedValue, medium.PersistedValue, "介质模式业务键不一致。");
        Verify.Equal(MainFlowInputs.WaterInjectionMediumMode.DisplayName, medium.DisplayName, "介质模式显示文字不一致。");
        Verify.Equal(MainFlowInputs.WashCount, _waferInformation.SelectWashCount(MainFlowInputs.WashCount), "清洗次数回读不一致。");
        AssertNoMessageBox();
    }

    /// <summary>05.05—05.08 的时间输入及“无范围提示”检查。</summary>
    /// <param name="milliseconds">固定毫秒值。</param>
    /// <param name="setter">页面输入方法。</param>
    private void SetTimeAndAssertNoMessage(int milliseconds, Action<int> setter)
    {
        setter(milliseconds);
        AssertNoMessageBox();
    }

    /// <summary>05.09 处理风刀字段并保存，等待本轮晶圆信息字段落盘。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SaveWaferInformation(StepContext context)
    {
        bool materialStatusVisible = _waferInformation.IsMaterialStatusBeforeCuttingVisible();
        if (materialStatusVisible)
        {
            _waferInformation.EnsureMaterialStatusBeforeCutting(false);
            context.Note("风刀字段可见，已按切割后固定片取消勾选。");
        }
        else
        {
            context.Note("风刀字段当前隐藏，按用例记录隐藏，未推断物料状态。");
        }

        _waferInformation.Save();
        string fileName = _recipeAName + ".rcp";
        string recipeFile = Path.Combine(_settings.RecipeRoot, fileName);
        WaitForWaferInformationSaved(recipeFile, materialStatusVisible);
        AssertNoMessageBox();
        _ui.Wait(
            () => string.Equals(_recipeEditor.ReadCurrentRecipeName(), fileName, StringComparison.Ordinal),
            "保存后当前编辑配方仍为 " + fileName);
        _artifacts.Log("05 晶圆信息保存完成；当前配方仍为：" + recipeFile);
    }

    /// <summary>轮询本轮配方 XML，直到 05 章固定输入全部写入；保存失败提示按最终失败处理。</summary>
    /// <param name="recipeFile">配方路径。</param>
    /// <param name="verifyMaterialStatus">是否同时核对风刀未勾选已落盘。</param>
    private void WaitForWaferInformationSaved(string recipeFile, bool verifyMaterialStatus)
    {
        string washCount = MainFlowInputs.WashCount.ToString(CultureInfo.InvariantCulture);
        _ui.Wait(
            () =>
            {
                AssertNoMessageBox();
                XDocument document;
                try
                {
                    document = XDocument.Load(recipeFile, LoadOptions.PreserveWhitespace);
                }
                catch (IOException)
                {
                    return false;
                }
                catch (System.Xml.XmlException)
                {
                    return false;
                }

                XElement? root = document.Root;
                if (root == null)
                {
                    return false;
                }

                return ReadRecipeField(root, "InspectType") == MainFlowInputs.InspectType.PersistedValue
                       && ReadRecipeField(root, "DeviceID") == MainFlowInputs.DeviceId
                       && ReadRecipeField(root, "StepID") == MainFlowInputs.StepId
                       && ReadRecipeField(root, "IsSimulationMode") == "false"
                       && ReadRecipeField(root, "WaterInjectionMediumMode") ==
                       MainFlowInputs.WaterInjectionMediumMode.PersistedValue
                       && ReadRecipeField(root, "WashCount") == washCount
                       && ReadRecipeField(root, "BlowDryingInjectWaterDelay") == FormatInteger(MainFlowInputs.InjectionWashTime)
                       && ReadRecipeField(root, "BlowDryingVacWaterDelay") == FormatInteger(MainFlowInputs.PostInjectionSoakTime)
                       && ReadRecipeField(root, "BlowDryingDelay") == FormatInteger(MainFlowInputs.WaferSoakTime)
                       && ReadRecipeField(root, "VacWaferTime") == FormatInteger(MainFlowInputs.VacuumTime)
                       && (!verifyMaterialStatus ||
                           ReadRecipeField(root, "TheMaterialStatusIsBeforeCutting") == "false");
            },
            "配方 A 的晶圆信息字段保存到磁盘");
    }

    /// <summary>读取配方 XML 根下指定字段。</summary>
    /// <param name="root">根元素。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字段文本。</returns>
    private static string ReadRecipeField(XElement root, string name) => root.Element(name)?.Value.Trim() ?? string.Empty;

    /// <summary>整数转为无区域格式文本。</summary>
    /// <param name="value">整数。</param>
    /// <returns>文本。</returns>
    private static string FormatInteger(int value) => value.ToString(CultureInfo.InvariantCulture);
}
