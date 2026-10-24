using System.Globalization;
using System.Xml.Linq;
using FlaUI.Core.AutomationElements;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>主流程 04：新建配方 A，联合核对界面与磁盘；不操作隐藏的晶圆类型字段。</summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>按原文顺序执行七个新建步骤，固定输入均回读后再提交。</summary>
    private void RunRecipeSection()
    {
        AssertRecipePreconditions();
        Step("04.01", OpenNewRecipe);
        if (_stoppedStatus != null) return;
        Step("04.02", _ => _newRecipe.SelectFileType(MainFlowInputs.RecipeFileType));
        if (_stoppedStatus != null) return;
        Step("04.03", _ => _newRecipe.SetRecipeName(_recipeAName));
        if (_stoppedStatus != null) return;
        Step("04.04", _ => _newRecipe.SelectCreateLocation(MainFlowInputs.CreateLocation));
        if (_stoppedStatus != null) return;
        Step("04.05", _ => _newRecipe.SelectSetupMode(MainFlowInputs.SetupMode));
        if (_stoppedStatus != null) return;
        Step("04.06", _ => _newRecipe.SelectWaferSize(MainFlowInputs.OuterDiameter, MainFlowInputs.FiducialType));
        if (_stoppedStatus != null) return;
        Step("04.07", _ => SubmitAndAssertRecipe(_recipeAName));
    }

    /// <summary>启动前只读检查配方根目录及同名产物；同名对象存在时阻塞，不覆盖、不删除。</summary>
    private void AssertRecipePreconditions()
    {
        _recipeAName = MainFlowInputs.RecipeAPrefix + "_" +
                       DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        if (!SamePath(_settings.RecipeRoot, MainFlowInputs.RecipeRoot))
        {
            BlockFirstStep(
                $"04.04 要求配方根目录为 {MainFlowInputs.RecipeRoot}，本轮配置为 {_settings.RecipeRoot}；请先统一测试部署。");
            return;
        }

        foreach (string suffix in new[] { ".rcp", string.Empty, ".sinf" })
        {
            string path = Path.Combine(_settings.RecipeRoot, _recipeAName + suffix);
            if (File.Exists(path) || Directory.Exists(path))
            {
                BlockFirstStep("上轮同名对象尚未归档，测试不会覆盖或删除：" + path);
                return;
            }
        }
    }

    /// <summary>进入配方页并打开新建窗口；吸附提醒按策略处理，严格策略不能盲目确认。</summary>
    /// <param name="context">步骤上下文。</param>
    private void OpenNewRecipe(StepContext context)
    {
        _startup.OpenRecipeEditor(_main);
        _recipeEditor = new RecipeEditorPage(_main, _settings.UiTimeout, _artifacts.Log);
        _newRecipe = _recipeEditor.OpenNewRecipeDialog(_ui, () =>
        {
            if (ManualHardware)
            {
                context.Confirm(
                    "程序提示“检测平台上未检测到晶圆吸附，请谨慎操作！”：核对运动平台上确实没有晶圆，" +
                    "确认后脚本将点击提示框“确定”继续新建。完成后在网页选择“已完成”。");
                return;
            }

            throw Verify.Blocked(
                "出现“检测平台上未检测到晶圆吸附”提醒；严格策略没有可观察的“平台无片”依据，" +
                "不能盲目确认该提示。请改用人工硬件策略，或先按现场规程清空平台后重跑。");
        });
        _newRecipe.AssertBasicInformationReady();
        context.Note(_newRecipe.AdsorptionWarningConfirmed ? "已确认吸附提醒后新建。" : "未出现吸附提醒。");
    }

    /// <summary>提交前再次防止同名覆盖；新建后同时校验 UI 身份与本轮文件。</summary>
    /// <param name="recipeName">不含扩展名的配方名称。</param>
    private void SubmitAndAssertRecipe(string recipeName)
    {
        string fileName = recipeName + ".rcp";
        string recipeFile = Path.Combine(_settings.RecipeRoot, fileName);
        string resourceDirectory = Path.Combine(_settings.RecipeRoot, recipeName);
        Verify.That(!File.Exists(recipeFile) && !Directory.Exists(resourceDirectory),
            "提交前发现同名配方对象，拒绝覆盖：" + recipeFile);

        DateTime submittedAt = DateTime.UtcNow;
        _newRecipe.Create();
        _ui.Wait(
            () =>
            {
                AssertNoMessageBox();
                return !_newRecipe.IsOpen;
            },
            "新建窗口关闭");
        _ui.Wait(
            () =>
            {
                AssertNoMessageBox();
                return string.Equals(_recipeEditor.ReadCurrentRecipeName(), fileName, StringComparison.Ordinal);
            },
            "当前编辑配方为 " + fileName);
        _recipeEditor.WaitForRecipeTreeItem(recipeName);
        _ui.Wait(() => _main.IsEnabled, "自动打开配方后主窗口可用");
        _ui.AssertSelected("WaferInformation.OuterDiameter", MainFlowInputs.OuterDiameter, _main);
        _ui.AssertSelected("WaferInformation.SetupMode", MainFlowInputs.SetupMode, _main);

        _ui.Wait(
            () => File.Exists(recipeFile) && new FileInfo(recipeFile).Length > 0 &&
                  Directory.Exists(resourceDirectory),
            "本轮非空 rcp 文件及同名资源目录生成");
        AssertRecipeFileFiducialType(recipeFile, MainFlowInputs.FiducialType);
        Verify.That(
            File.GetLastWriteTimeUtc(recipeFile) >= submittedAt.AddSeconds(-2),
            "配方文件不是本轮提交后生成，请检查落盘路径。");
        AssertNoMessageBox();
        _artifacts.Log("配方树、当前配方名、自动打开页属性、非空文件及资源目录均已检查；保留：" + recipeFile);
    }

    /// <summary>从落盘 XML 回读缺口类型，替代被产品隐藏的主配方页控件回读。</summary>
    /// <param name="recipeFile">配方路径。</param>
    /// <param name="expected">期望缺口类型。</param>
    private static void AssertRecipeFileFiducialType(string recipeFile, string expected)
    {
        XDocument document = XDocument.Load(recipeFile, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ??
                        throw Verify.Failed("配方 XML 缺少根元素：" + recipeFile);
        XElement element = root.Element("FiducialType") ??
                           throw Verify.Failed("配方 XML 缺少 FiducialType 节点：" + recipeFile);
        Verify.Equal(expected, element.Value.Trim(), "配方落盘缺口类型与新建时的选择不一致：" + recipeFile);
    }

    /// <summary>提交后的任何消息框都不能当作创建成功或盲目确认。</summary>
    private void AssertNoMessageBox()
    {
        AutomationElement? message = _ui.Optional("MessageBox.Content");
        Verify.That(message == null, "出现提示：" + (message?.Name ?? string.Empty));
    }

    /// <summary>是否存在消息框。</summary>
    /// <returns>存在时为真。</returns>
    private bool HasMessageBox() => _ui.Optional("MessageBox.Content") != null;

    /// <summary>规范化后比较两个目录路径，忽略大小写与尾分隔符。</summary>
    /// <param name="left">路径一。</param>
    /// <param name="right">路径二。</param>
    /// <returns>指向同一目录时为真。</returns>
    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
