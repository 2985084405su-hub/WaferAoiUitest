using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 晶圆信息页（05）：稳定定位、参数配置、保存与页面回读。
/// 迁移自源工程 Pages/WaferInformationPage.cs 与 .MainFlow.cs；断言出口改为 Verify。
/// </summary>
internal sealed class WaferInformationPage
{
    /// <summary>产品 ID 输入框。</summary>
    private const string DeviceIdField = "WaferInformation.DeviceId";

    /// <summary>工序 ID 输入框。</summary>
    private const string StepIdField = "WaferInformation.StepId";

    /// <summary>主窗口。</summary>
    private readonly Window _window;

    /// <summary>默认超时。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>创建页面对象。</summary>
    /// <param name="window">主窗口。</param>
    /// <param name="timeout">默认超时。</param>
    public WaferInformationPage(Window window, TimeSpan timeout)
    {
        _window = window;
        _timeout = timeout;
    }

    /// <summary>等待晶圆尺寸控件出现，确认页面已加载。</summary>
    public void WaitUntilLoaded() => _ = FindRequired("WaferInformation.OuterDiameter");

    /// <summary>05.01 选择检测类型并回读实际选中项。</summary>
    /// <param name="option">检测类型。</param>
    /// <returns>实际选中项。</returns>
    public UiSelectionValue SelectInspectionType(UiSelectionValue option)
    {
        SelectOption("WaferInformation.InspectType", option.PersistedValue, option.DisplayName);
        return ReadSelectedOption("WaferInformation.InspectType");
    }

    /// <summary>05.02 写入产品 ID。</summary>
    /// <param name="value">产品 ID。</param>
    public void SetDeviceId(string value) => SetText(DeviceIdField, value);

    /// <summary>05.02 写入工序 ID。</summary>
    /// <param name="value">工序 ID。</param>
    public void SetStepId(string value) => SetText(StepIdField, value);

    /// <summary>读取产品 ID。</summary>
    /// <returns>文本。</returns>
    public string ReadDeviceId() => ReadText(DeviceIdField);

    /// <summary>读取工序 ID。</summary>
    /// <returns>文本。</returns>
    public string ReadStepId() => ReadText(StepIdField);

    /// <summary>把焦点移出当前编辑框，等价于手工用例的“输入后离焦”。</summary>
    public void CommitInput() => FindRequired(DeviceIdField).AsTextBox().Focus();

    /// <summary>05.03 按期望状态设置仿真模式并回读。</summary>
    /// <param name="enabled">期望勾选状态。</param>
    public void EnsureSimulationMode(bool enabled)
    {
        CheckBox toggle = FindRequired("WaferInformation.SimulationMode").AsCheckBox();
        if (toggle.IsChecked != enabled)
        {
            toggle.IsChecked = enabled;
        }

        UiWait.Until(
            () => FindRequired("WaferInformation.SimulationMode").AsCheckBox().IsChecked == enabled,
            _timeout,
            "仿真模式勾选状态为 " + enabled);
    }

    /// <summary>读取仿真模式勾选状态。</summary>
    /// <returns>已勾选为真。</returns>
    public bool ReadSimulationMode() =>
        FindRequired("WaferInformation.SimulationMode").AsCheckBox().IsChecked == true;

    /// <summary>05.04—05.08 前提：三代机吹干配置区域必须可见。</summary>
    public void AssertBlowDryingAvailable()
    {
        AutomationElement group = FindRequired("WaferInformation.BlowDryConfig");
        Verify.Available(
            !group.IsOffscreen,
            "三代机吹干配置区域当前不可见，环境不满足 05.04—05.08 的三代机前提。");
    }

    /// <summary>05.04 选择介质模式并回读。</summary>
    /// <param name="option">介质模式。</param>
    /// <returns>实际选中项。</returns>
    public UiSelectionValue SelectWaterInjectionMedium(UiSelectionValue option)
    {
        SelectOption("WaferInformation.WaterInjectionMediumMode", option.PersistedValue, option.DisplayName);
        return ReadSelectedOption("WaferInformation.WaterInjectionMediumMode");
    }

    /// <summary>05.04 选择清洗次数；缺少目标值时按环境不匹配阻塞。</summary>
    /// <param name="count">次数。</param>
    /// <returns>实际次数。</returns>
    public int SelectWashCount(int count)
    {
        const string automationId = "WaferInformation.WashCount";
        string countText = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string optionId = automationId + ".Option." + countText;
        ComboBox comboBox = FindRequired(automationId).AsComboBox();
        comboBox.Expand();
        try
        {
            WaitForValidOptionAutomationIds(comboBox, automationId);
            if (!comboBox.Items.Any(item => string.Equals(item.AutomationId, optionId, StringComparison.Ordinal)))
            {
                throw Verify.Blocked("当前设备配置的清洗次数选项不含 " + count + "，环境不匹配，不回退到其他次数。");
            }
        }
        finally
        {
            comboBox.Collapse();
        }

        SelectOption(automationId, countText, countText);
        return ReadWashCount();
    }

    /// <summary>读取清洗次数。</summary>
    /// <returns>次数。</returns>
    public int ReadWashCount()
    {
        UiSelectionValue selected = ReadSelectedOption("WaferInformation.WashCount");
        if (!int.TryParse(selected.PersistedValue, out int count))
        {
            throw Verify.Failed("清洗次数选项业务键不是整数：" + selected.PersistedValue);
        }

        return count;
    }

    /// <summary>05.05 写入注水冲洗时间并失焦回读。</summary>
    /// <param name="milliseconds">毫秒。</param>
    public void SetInjectionWashTime(int milliseconds) =>
        SetTimeField("WaferInformation.BlowDryingInjectWaterDelay", milliseconds);

    /// <summary>05.06 写入注水后吸水时间并失焦回读。</summary>
    /// <param name="milliseconds">毫秒。</param>
    public void SetPostInjectionSoakTime(int milliseconds) =>
        SetTimeField("WaferInformation.BlowDryingVacWaterDelay", milliseconds);

    /// <summary>05.07 写入吸附晶圆后的吸水时间并失焦回读。</summary>
    /// <param name="milliseconds">毫秒。</param>
    public void SetWaferSoakTime(int milliseconds) =>
        SetTimeField("WaferInformation.BlowDryingDelay", milliseconds);

    /// <summary>05.08 写入吸附晶圆的真空时间并失焦回读。</summary>
    /// <param name="milliseconds">毫秒。</param>
    public void SetVacuumTime(int milliseconds) =>
        SetTimeField("WaferInformation.VacWaferTime", milliseconds);

    /// <summary>05.09 风刀字段是否可见。</summary>
    /// <returns>存在且不在屏幕外。</returns>
    public bool IsMaterialStatusBeforeCuttingVisible()
    {
        AutomationElement? field = _window.FindFirstDescendant(
            condition => condition.ByAutomationId("WaferInformation.MaterialStatusBeforeCutting"));
        return field != null && !field.IsOffscreen;
    }

    /// <summary>05.09 风刀字段可见时设置勾选状态并回读。</summary>
    /// <param name="isChecked">期望状态。</param>
    public void EnsureMaterialStatusBeforeCutting(bool isChecked)
    {
        const string automationId = "WaferInformation.MaterialStatusBeforeCutting";
        CheckBox checkBox = FindRequired(automationId).AsCheckBox();
        if (checkBox.IsChecked != isChecked)
        {
            checkBox.IsChecked = isChecked;
        }

        UiWait.Until(
            () => FindRequired(automationId).AsCheckBox().IsChecked == isChecked,
            _timeout,
            "风刀字段勾选状态为 " + isChecked);
    }

    /// <summary>点击保存：先转移焦点提交最后一个输入值。</summary>
    public void Save()
    {
        AutomationElement saveButton = FindRequired("RecipeStep.Save");
        Verify.That(saveButton.IsEnabled, "晶圆信息步骤的保存按钮当前不可用。");
        saveButton.Focus();
        saveButton.AsButton().Invoke();
    }

    /// <summary>写入时间输入框并失焦回读。</summary>
    /// <param name="automationId">控件 ID。</param>
    /// <param name="milliseconds">毫秒。</param>
    private void SetTimeField(string automationId, int milliseconds)
    {
        string value = milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetText(automationId, value);
        CommitInput();
        Verify.Equal(value, ReadText(automationId), "时间字段失焦后回读不一致：" + automationId);
    }

    /// <summary>按选项 ID 选择下拉项并等待回读稳定。</summary>
    /// <param name="automationId">组合框 ID。</param>
    /// <param name="optionKey">业务键。</param>
    /// <param name="displayName">显示文字。</param>
    private void SelectOption(string automationId, string optionKey, string displayName)
    {
        ComboBox comboBox = FindRequired(automationId).AsComboBox();
        comboBox.Expand();
        WaitForValidOptionAutomationIds(comboBox, automationId);
        string optionAutomationId = $"{automationId}.Option.{optionKey}";

        ComboBoxItem selectedItem = UiWait.UntilNotNull(
            () => comboBox.Items.FirstOrDefault(item =>
                string.Equals(item.AutomationId, optionAutomationId, StringComparison.Ordinal) &&
                item.IsEnabled &&
                !item.IsOffscreen),
            _timeout,
            $"控件 '{automationId}' 中选项 '{optionAutomationId}' 可见");
        selectedItem.Select();
        comboBox.Collapse();

        UiWait.Until(
            () =>
            {
                ComboBox current = FindRequired(automationId).AsComboBox();
                ComboBoxItem? currentSelected = current.SelectedItem;
                if (currentSelected != null &&
                    string.Equals(currentSelected.AutomationId, optionAutomationId, StringComparison.Ordinal))
                {
                    return true;
                }

                return current.Items.Any(item =>
                    string.Equals(item.AutomationId, optionAutomationId, StringComparison.Ordinal) && item.IsSelected);
            },
            _timeout,
            $"控件 '{automationId}' 选中 '{displayName}'");
    }

    /// <summary>设置文本框内容并等待回读一致。</summary>
    /// <param name="automationId">控件 ID。</param>
    /// <param name="value">目标值。</param>
    private void SetText(string automationId, string value)
    {
        TextBox textBox = FindRequired(automationId).AsTextBox();
        textBox.Focus();
        textBox.Text = value;
        UiWait.Until(
            () => string.Equals(textBox.Text, value, StringComparison.Ordinal),
            _timeout,
            $"控件 '{automationId}' 的文本变为 '{value}'");
    }

    /// <summary>读取唯一选中项的业务键与显示文字。</summary>
    /// <param name="automationId">组合框 ID。</param>
    /// <returns>选中项。</returns>
    private UiSelectionValue ReadSelectedOption(string automationId)
    {
        AutomationElement combo = FindRequired(automationId);
        SoftwareUi.RequirePattern(combo.Patterns.Selection.IsSupported, automationId, "Selection");
        AutomationElement[] selected = combo.Patterns.Selection.Pattern.Selection.Value;
        if (selected.Length != 1)
        {
            throw Verify.Failed($"控件 '{automationId}' 必须恰好选中一项，实际为 {selected.Length} 项。");
        }

        AutomationElement option = selected[0];
        string prefix = automationId + ".Option.";
        string optionId = option.AutomationId;
        if (!optionId.StartsWith(prefix, StringComparison.Ordinal) || optionId.Length == prefix.Length)
        {
            throw Verify.Failed($"控件 '{automationId}' 的选中项业务标识无效：'{optionId}'。");
        }

        // 检测类型和介质模式使用对象包装，容器 Name 是 { Value = ..., Path = ... }；
        // 其余沿用已验证的简单文本 Name，子文本读取例外不扩展到其他控件。
        string displayName = automationId is "WaferInformation.InspectType" or "WaferInformation.WaterInjectionMediumMode"
            ? ReadOptionDisplayText(option)
            : option.Name;
        return new UiSelectionValue(displayName, optionId.Substring(prefix.Length));
    }

    /// <summary>读取选项内唯一文本元素。</summary>
    /// <param name="option">选项。</param>
    /// <returns>显示文本。</returns>
    private static string ReadOptionDisplayText(AutomationElement option)
    {
        AutomationElement[] texts = option.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
        if (texts.Length != 1)
        {
            throw Verify.Failed(
                $"选项 '{option.AutomationId}' 的显示文本读取失败：预期唯一 Text，实际 {texts.Length} 个。");
        }

        string text = texts[0].Name;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Verify.Failed($"选项 '{option.AutomationId}' 的显示文本为空。");
        }

        return text;
    }

    /// <summary>读取文本框当前文本。</summary>
    /// <param name="automationId">控件 ID。</param>
    /// <returns>去首尾空白的文本。</returns>
    private string ReadText(string automationId) => FindRequired(automationId).AsTextBox().Text.Trim();

    /// <summary>等待下拉框所有选项都公开唯一且前缀正确的 AutomationId。</summary>
    /// <param name="comboBox">已展开的组合框。</param>
    /// <param name="automationId">组合框 ID。</param>
    private void WaitForValidOptionAutomationIds(ComboBox comboBox, string automationId)
    {
        UiWait.Until(
            () =>
            {
                ComboBoxItem[] items = comboBox.Items;
                if (items.Length == 0)
                {
                    throw Verify.Failed($"控件 '{automationId}' 没有可用选项。");
                }

                string expectedPrefix = $"{automationId}.Option.";
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ComboBoxItem item in items)
                {
                    string optionId = item.AutomationId?.Trim() ?? string.Empty;
                    if (!optionId.StartsWith(expectedPrefix, StringComparison.Ordinal))
                    {
                        throw Verify.Failed(
                            $"控件 '{automationId}' 的选项 '{item.Name}' 未提供前缀为 '{expectedPrefix}' 的 AutomationId，" +
                            $"实际值为 '{optionId}'。");
                    }

                    if (!ids.Add(optionId))
                    {
                        throw Verify.Failed($"控件 '{automationId}' 存在重复的选项 AutomationId：'{optionId}'。");
                    }
                }

                return true;
            },
            _timeout,
            $"控件 '{automationId}' 的所有选项公开有效且唯一的 AutomationId");
    }

    /// <summary>按 ID 查找必需控件。</summary>
    /// <param name="automationId">控件 ID。</param>
    /// <returns>控件。</returns>
    private AutomationElement FindRequired(string automationId) =>
        UiWait.UntilNotNull(
            () => _window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
            _timeout,
            $"晶圆信息控件 '{automationId}' 出现");
}
