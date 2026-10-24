using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 新建配方窗口，只通过 RecipeNew.* AutomationId 操作；选项选择走 <see cref="OwnedUi.SelectExact"/>。
/// 迁移自源工程 Pages/NewRecipeDialog.cs。
/// </summary>
internal sealed class NewRecipeDialog
{
    /// <summary>绑定本轮进程的 UI 操作器。</summary>
    private readonly OwnedUi _ui;

    /// <summary>创建新建配方窗口对象。</summary>
    /// <param name="ui">绑定本轮进程的 UI 操作器。</param>
    /// <param name="adsorptionWarningConfirmed">打开窗口前是否确认过吸附提醒。</param>
    public NewRecipeDialog(OwnedUi ui, bool adsorptionWarningConfirmed)
    {
        _ui = ui;
        AdsorptionWarningConfirmed = adsorptionWarningConfirmed;
    }

    /// <summary>打开窗口前是否出现并确认了吸附提醒。</summary>
    public bool AdsorptionWarningConfirmed { get; }

    /// <summary>新建窗口是否仍打开。</summary>
    public bool IsOpen => _ui.Optional("RecipeNew.OK") != null;

    /// <summary>确认窗口已加载，且默认基础字段都有唯一选中项。</summary>
    public void AssertBasicInformationReady()
    {
        _ = _ui.Find("RecipeNew.Name");
        _ = _ui.Find("RecipeNew.OK");
        foreach (string automationId in new[]
                 {
                     "RecipeNew.Type",
                     "RecipeNew.CreateLocation",
                     "RecipeNew.SetupMode",
                     "RecipeNew.OuterDiameter",
                     "RecipeNew.WaferType",
                     "RecipeNew.FiducialType"
                 })
        {
            AutomationElement combo = _ui.Find(automationId);
            SoftwareUi.RequirePattern(combo.Patterns.Selection.IsSupported, automationId, "Selection");
            AutomationElement[] selected = combo.Patterns.Selection.Pattern.Selection.Value;
            Verify.Equal(1, selected.Length, $"新建配方基础字段 '{automationId}' 应恰好有一个默认选项。");
            Verify.That(
                !string.IsNullOrWhiteSpace(selected[0].Name),
                $"新建配方基础字段 '{automationId}' 的默认选项没有可读名称。");
        }
    }

    /// <summary>选择文件类型（获准的精确选项文字）。</summary>
    /// <param name="fileType">选项文字。</param>
    public void SelectFileType(string fileType) => _ui.SelectExact("RecipeNew.Type", fileType);

    /// <summary>填写配方名称并等待完整回读。</summary>
    /// <param name="name">不含 .rcp 的名称。</param>
    public void SetRecipeName(string name)
    {
        Verify.That(!string.IsNullOrWhiteSpace(name), "配方名称不能为空。");
        _ui.SetValue("RecipeNew.Name", name);
    }

    /// <summary>选择创建位置。</summary>
    /// <param name="location">选项文字。</param>
    public void SelectCreateLocation(string location) => _ui.SelectExact("RecipeNew.CreateLocation", location);

    /// <summary>选择建档模式。</summary>
    /// <param name="setupMode">选项文字。</param>
    public void SelectSetupMode(string setupMode) => _ui.SelectExact("RecipeNew.SetupMode", setupMode);

    /// <summary>选择晶圆尺寸与缺口类型并回读。</summary>
    /// <param name="outerDiameter">尺寸选项。</param>
    /// <param name="fiducialType">缺口类型选项。</param>
    public void SelectWaferSize(string outerDiameter, string fiducialType)
    {
        _ui.SelectExact("RecipeNew.OuterDiameter", outerDiameter);
        _ui.SelectExact("RecipeNew.FiducialType", fiducialType);
    }

    /// <summary>提交新建窗口。</summary>
    public void Create() => _ui.Invoke("RecipeNew.OK");
}

/// <summary>
/// 配方编辑器：搜索、选择、打开、保存与当前配方回读。
/// 迁移自源工程 Pages/RecipeEditorPage.cs。
/// </summary>
internal sealed class RecipeEditorPage
{
    /// <summary>平台无吸附时的已知提醒原文。</summary>
    public const string AdsorptionWarning = "检测平台上未检测到晶圆吸附，请谨慎操作！";

    /// <summary>主窗口。</summary>
    private readonly Window _window;

    /// <summary>默认超时。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>日志。</summary>
    private readonly Action<string> _log;

    /// <summary>创建配方编辑页对象。</summary>
    /// <param name="window">主窗口。</param>
    /// <param name="timeout">默认超时。</param>
    /// <param name="log">日志。</param>
    public RecipeEditorPage(Window window, TimeSpan timeout, Action<string> log)
    {
        _window = window;
        _timeout = timeout;
        _log = log;
    }

    /// <summary>等待配方搜索框出现。</summary>
    public void WaitUntilLoaded() => _ = FindRequiredByAutomationId("Recipe.Search");

    /// <summary>
    /// 点击“新建”，等待新建窗口；若先出现已知吸附提醒，执行确认回调后再点“确定”。
    /// 其他弹窗都不能自动确认。
    /// </summary>
    /// <param name="ui">UI 操作器。</param>
    /// <param name="beforeConfirmAdsorptionWarning">点“确定”前的核对动作。</param>
    /// <returns>已加载的新建窗口。</returns>
    public NewRecipeDialog OpenNewRecipeDialog(OwnedUi ui, Action beforeConfirmAdsorptionWarning)
    {
        ui.Invoke("Recipe.New", _window);

        AutomationElement? warning = null;
        ui.Wait(
            () =>
            {
                warning = ui.Optional("MessageBox.Content");
                return warning != null || ui.Optional("RecipeNew.Name") != null;
            },
            "新建配方窗口或吸附提醒出现");
        if (warning != null)
        {
            Verify.Equal(AdsorptionWarning, warning.Name, "点击新建后出现非预期弹窗，不能自动确认。");
            beforeConfirmAdsorptionWarning();
            _log("确认已知的吸附提醒，继续软件新建，不执行上片或运动。");
            ui.Invoke("MessageBox.OK");
        }

        _ = ui.Find("RecipeNew.Name");
        return new NewRecipeDialog(ui, warning != null);
    }

    /// <summary>等待配方树出现指定配方的唯一条目。</summary>
    /// <param name="recipeName">不含 .rcp 的名称。</param>
    public void WaitForRecipeTreeItem(string recipeName)
    {
        _ = UiWait.UntilNotNull(
            () => FindUniqueRecipeTreeItem(recipeName + ".rcp", recipeName),
            _timeout,
            $"配方树中出现配方 '{recipeName}'");
    }

    /// <summary>点击保存按钮。</summary>
    public void SaveCurrentRecipe()
    {
        AutomationElement button = FindRequiredByAutomationId("RecipeStep.Save");
        SoftwareUi.RequirePattern(button.Patterns.Invoke.IsSupported, "RecipeStep.Save", "Invoke");
        Verify.That(button.IsEnabled, "配方保存按钮当前不可用。");
        button.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>读取标题中的当前配方名。</summary>
    /// <returns>显示名。</returns>
    public string ReadCurrentRecipeName() => FindRequiredByAutomationId("Recipe.CurrentRecipeName").Name;

    /// <summary>按 ID 查找必需元素。</summary>
    /// <param name="automationId">AutomationId。</param>
    /// <returns>元素。</returns>
    private AutomationElement FindRequiredByAutomationId(string automationId) =>
        SoftwareUi.Find(_window, automationId, _timeout);

    /// <summary>仅配方树使用本轮获准的名称定位例外，按 RuntimeId 去重并拒绝同名不同条目。</summary>
    /// <param name="names">接受的名称。</param>
    /// <returns>唯一树条目。</returns>
    private TreeItem? FindUniqueRecipeTreeItem(params string[] names)
    {
        AutomationElement tree = FindRequiredByAutomationId("tvMainTree");
        var items = new Dictionary<string, TreeItem>(StringComparer.Ordinal);
        foreach (AutomationElement named in tree.FindAllDescendants().Where(element => HasAnyName(element, names)))
        {
            AutomationElement? current = named;
            while (current != null && !current.Equals(tree) && current.ControlType != ControlType.TreeItem)
            {
                current = current.Parent;
            }

            if (current != null && !current.Equals(tree))
            {
                string runtimeId = string.Join(",", current.Properties.RuntimeId.Value);
                items[runtimeId] = current.AsTreeItem();
            }
        }

        if (items.Count > 1)
        {
            throw Verify.Blocked($"配方名称匹配到{items.Count}个不同条目：{string.Join(" / ", names)}");
        }

        TreeItem? result = items.Values.Count == 0 ? null : items.Values.First();
        if (result != null)
        {
            SoftwareUi.RequirePattern(result.Patterns.SelectionItem.IsSupported, "配方树获准名称定位条目", "SelectionItem");
        }

        return result;
    }

    /// <summary>检查可访问名称，忽略不暴露 Name 的 provider。</summary>
    /// <param name="element">候选元素。</param>
    /// <param name="names">接受名称。</param>
    /// <returns>命中时为真。</returns>
    private static bool HasAnyName(AutomationElement element, string[] names)
    {
        try
        {
            return names.Contains(element.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (PropertyNotSupportedException)
        {
            return false;
        }
    }
}
