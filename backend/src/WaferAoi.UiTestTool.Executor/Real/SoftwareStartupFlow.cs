using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 启动流程：等待登录窗口、登录、处理启动回零提示并进入配方页；目标只通过 AutomationId 定位。
/// 迁移自源工程 Windows/SoftwareStartupFlow.cs；回零提示新增“严格策略确认并等待回零完成”分支。
/// </summary>
internal sealed class SoftwareStartupFlow
{
    /// <summary>绑定本轮进程的 UI 操作器。</summary>
    private readonly OwnedUi _ui;

    /// <summary>账号、密码及各阶段超时。</summary>
    private readonly ExecutorSettings _settings;

    /// <summary>不含密码的步骤日志。</summary>
    private readonly Action<string> _log;

    /// <summary>创建启动流程。</summary>
    /// <param name="ui">绑定本轮进程的 UI 操作器。</param>
    /// <param name="settings">账号、密码及超时。</param>
    /// <param name="log">不含密码的日志。</param>
    public SoftwareStartupFlow(OwnedUi ui, ExecutorSettings settings, Action<string> log)
    {
        _ui = ui;
        _settings = settings;
        _log = log;
    }

    /// <summary>等待硬件状态登录窗口的账号框和登录按钮出现。</summary>
    public void WaitForLoginWindow()
    {
        _ = _ui.Find(OwnedUi.LoginAccountId, _settings.StartupTimeout);
        _ = _ui.Find("HardwareStatus.Login");
    }

    /// <summary>在 ID 限定的账号框内精确选择配置账号并登录。</summary>
    /// <returns>登录后出现的主窗口。</returns>
    public Window Login()
    {
        _ui.SelectExact(OwnedUi.LoginAccountId, _settings.UserName);
        _ui.SetValue("HardwareStatus.Password", _settings.Password, secret: true);
        _log($"提交{_settings.UserName}登录，密码不记录、不回读。");
        _ui.Invoke("HardwareStatus.Login");
        _ui.Wait(
            () =>
            {
                AutomationElement? error = _ui.Optional("HardwareStatus.ErrorMessage");
                if (error != null && !string.IsNullOrWhiteSpace(error.Name))
                {
                    throw Verify.Failed("登录未成功，界面已显示错误；详见失败截图。");
                }

                return _ui.Optional("HardwareStatus.Login") == null;
            },
            $"{_settings.UserName}登录成功，登录窗口关闭",
            _settings.StartupTimeout);
        return _ui.Find("main", _settings.StartupTimeout).AsWindow();
    }

    /// <summary>等待启动回零提示出现，并核对它确实是“全部轴回零”提示。</summary>
    public void AssertStartupHomingPrompt()
    {
        string message = _ui.Find("MessageBox.Content", _settings.StartupPromptTimeout).Name;
        Verify.That(
            message.Contains("全部轴", StringComparison.Ordinal) && message.Contains("回零", StringComparison.Ordinal),
            "启动后出现非预期弹窗，停止自动处理：" + message);
    }

    /// <summary>确认启动回零提示，触发设备回零。</summary>
    public void AcceptStartupHoming()
    {
        AssertStartupHomingPrompt();
        _log("通过 MessageBox.OK 确认启动“全部轴回零”提示。");
        _ui.Invoke("MessageBox.OK");
        _ui.Wait(() => _ui.Optional("MessageBox.Content") == null, "启动回零提示关闭", _settings.StartupPromptTimeout);
    }

    /// <summary>取消启动回零提示；取消后设备未回零，后续不得执行依赖回零的硬件动作。</summary>
    public void CancelStartupHoming()
    {
        AssertStartupHomingPrompt();
        _log("通过 MessageBox.Cancel 取消启动回零提示。");
        _ui.Invoke("MessageBox.Cancel");
        _ui.Wait(() => _ui.Optional("MessageBox.Content") == null, "启动回零提示关闭");
    }

    /// <summary>
    /// 在已有顶层窗口中查找回零完成的可读状态。
    /// 返回全部命中的名称文本，供“回零完成”判定与证据记录；找不到时返回空集合。
    /// </summary>
    /// <param name="application">本轮程序驱动。</param>
    /// <returns>命中文本。</returns>
    public IReadOnlyList<string> ReadHomingStateTexts(WaferAoiApplication application)
    {
        var found = new List<string>();
        foreach (Window window in application.GetTopLevelWindows())
        {
            AutomationElement[] all;
            try
            {
                all = window.FindAllDescendants();
            }
            catch (Exception exception) when (exception is PropertyNotSupportedException or COMException)
            {
                continue;
            }

            foreach (AutomationElement element in all)
            {
                string? name;
                try
                {
                    name = element.Name;
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (name!.Contains("回零", StringComparison.Ordinal) ||
                    name.Contains("Homing", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(name);
                }
            }
        }

        return found;
    }

    /// <summary>点击主导航“配方”，等待配方搜索框出现。</summary>
    /// <param name="main">已登录的主窗口。</param>
    public void OpenRecipeEditor(Window main)
    {
        _ui.Invoke("Main.Recipe", main);
        _ = _ui.Find("Recipe.Search", root: main);
        _log("已进入配方编辑页。");
    }
}
