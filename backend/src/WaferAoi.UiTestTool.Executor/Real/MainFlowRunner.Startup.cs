using System.Diagnostics;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>主流程 01—03：启动、登录、版本核对、回零提示处理、放片与端口选择。</summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>按文档编号执行 01—03；01.02 依赖登录后才生成的主窗口标题，保留原编号延后执行。</summary>
    private void RunStartupSection()
    {
        Step("01.01", _ => StartAndAssertLogin());
        if (_stoppedStatus != null) return;
        Step("02.01", _ => LoginToMainWindow());
        if (_stoppedStatus != null) return;
        Step("02.02", HandleStartupHoming);
        if (_stoppedStatus != null) return;
        Step("01.02", AssertInstallation);
        if (_stoppedStatus != null) return;
        Step("03.01", PrepareWafer);
        if (_stoppedStatus != null) return;
        Step("03.02", SelectLoadport);
    }

    /// <summary>启动被测程序，断言只有本轮 PID，并等待登录控件。</summary>
    private void StartAndAssertLogin()
    {
        _application = new WaferAoiApplication(_settings, _artifacts.Log);
        _application.Start();
        _ui = new OwnedUi(_application, _settings, MainFlowInputs.ApprovedSelections, _artifacts.Log);
        _startup = new SoftwareStartupFlow(_ui, _settings, _artifacts.Log);
        _startup.WaitForLoginWindow();

        string processName = Path.GetFileNameWithoutExtension(_settings.ApplicationPath);
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            Verify.Equal(1, processes.Length, "不得重复启动多个主程序。");
            Verify.That(
                processes[0].Id == _application.ProcessId,
                "实际主程序不属于本次启动：期望 PID=" + _application.ProcessId + "，实际 PID=" + processes[0].Id + "。");
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        _artifacts.CaptureScreen("01.01启动登录窗口", "01.01");
    }

    /// <summary>选择账号并登录，等待主窗口出现。</summary>
    private void LoginToMainWindow()
    {
        _main = _startup.Login();
        Verify.That(_main.IsEnabled, "登录后主窗口不可用，不能继续操作。");
        _artifacts.Log("已登录并进入主界面。");
    }

    /// <summary>
    /// 02.02 处理“全部轴回零”提示。
    /// 跳过策略：取消回零并把硬件检查记为跳过；人工策略：等待现场处理并确认；
    /// 严格策略：自动确认并核对可读的回零状态，读不到状态时如实阻塞（不以“无异常”代替回零完成）。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void HandleStartupHoming(StepContext context)
    {
        if (SkipHardware)
        {
            _startup.CancelStartupHoming();
            context.SkipHardwarePart("已取消全部轴回零；相机、轴、EFEM、光源、报警及回零完成情况未验证。");
            return;
        }

        if (ManualHardware)
        {
            _startup.AssertStartupHomingPrompt();
            context.Confirm(
                "在程序内点击“全部轴回零”提示的确定，等待回零完成；核对本次参与的相机、轴、EFEM、光源正常，" +
                "报警区没有阻断动作的报警。完成后在网页选择“已完成”。");
            _ui.Wait(
                () => _ui.Optional("MessageBox.Content") == null,
                "启动回零提示已处理",
                _settings.StartupPromptTimeout);
            _hardwareActuated = true;
            return;
        }

        // 严格策略：自动确认，触发回零。
        _startup.AcceptStartupHoming();
        _hardwareActuated = true;

        IReadOnlyList<string> states = _startup.ReadHomingStateTexts(_application!);
        if (states.Count > 0)
        {
            _artifacts.WriteText(
                "02.02回零相关界面文本",
                string.Join(Environment.NewLine, states),
                "02.02");
        }

        bool completed = states.Any(text =>
            (text.Contains("回零", StringComparison.Ordinal) || text.Contains("Homing", StringComparison.OrdinalIgnoreCase)) &&
            (text.Contains("完成", StringComparison.Ordinal) || text.Contains("已回零", StringComparison.Ordinal) ||
             text.Contains("Done", StringComparison.OrdinalIgnoreCase)));
        Verify.Available(
            completed,
            states.Count == 0
                ? "已确认“全部轴回零”提示，但界面上找不到可读的回零完成状态（未发现含“回零”的可见文本）；" +
                  "严格策略不能以“提示已关闭”代替回零完成，请改用人工硬件策略或提供该状态的可读来源。"
                : "已确认“全部轴回零”提示，但可见的回零相关文本不足以判定完成：" +
                  string.Join(" | ", states.Take(8)));
        Verify.That(!HasMessageBox(), "回零后出现阻断弹窗，停止后续操作。");
        context.Note("已自动确认回零并读到回零完成状态。");
    }

    /// <summary>标题中的版本、编译信息与本轮安装记录逐项精确比较，并保存截图。</summary>
    private void AssertInstallation(StepContext context)
    {
        if (_manifest.Options.SkipVersionAssertion)
        {
            context.SkipByUser("本轮选择跳过版本断言（版本号与编译日期）；未核对安装记录，不计为通过。");
            return;
        }

        Verify.Available(
            _settings.ExpectedVersion.Length > 0 && _settings.ExpectedBuildDate.Length > 0,
            "缺少本轮安装记录：请在配置中填写预期版本号与编译日期（或勾选跳过版本断言）。");

        string title = string.Empty;
        _ui.Wait(
            () =>
            {
                title = _main.Title;
                return title.Contains("版本号：", StringComparison.Ordinal) &&
                       title.Contains("编译日期：", StringComparison.Ordinal);
            },
            "主窗口版本和编译日期显示完整");
        _artifacts.CaptureElement(_main, "01.02版本信息", "01.02");
        _artifacts.Log("01.02 实际主窗口标题：" + title);
        Match match = Regex.Match(
            title,
            @"版本号：(?<version>.*?)\s{2,}编译日期：(?<date>\d{4}-\d{2}-\d{2} \d{2}:\d{2})$");
        Verify.That(match.Success, "主窗口版本及编译信息格式不完整：" + title);
        Verify.Equal(_settings.ExpectedVersion, match.Groups["version"].Value, "软件版本与本轮安装记录不符。");
        Verify.Equal(_settings.ExpectedBuildDate, match.Groups["date"].Value, "编译日期与本轮安装记录不符。");
        context.Note("版本号=" + match.Groups["version"].Value + "；编译日期=" + match.Groups["date"].Value);
    }

    /// <summary>03.01 记录本轮开始时间；实物放片由现场完成。</summary>
    private void PrepareWafer(StepContext context)
    {
        _runStartedAt = DateTimeOffset.Now;
        context.Note("本轮开始时间=" + _runStartedAt.ToString("O"));
        if (SkipHardware)
        {
            context.SkipWholeStep(
                "放片、装盒与工位空片检查是现场物理操作，本轮按跳过硬件策略未执行；" +
                "记录开始时间=" + _runStartedAt.ToString("O"));
            return;
        }

        context.Confirm(
            "把固定测试片正面朝上放入 Loadport1 槽1，按现场正常装盒/关门完成准备；" +
            "确认运动平台、机械手、对准器均无片，然后记录本轮开始时间。完成后在网页选择“已完成”。");
    }

    /// <summary>03.02 任务→晶圆分布，选择 Loadport1，并核对槽位与实物一致。</summary>
    private void SelectLoadport(StepContext context)
    {
        OpenWaferMapTab();
        _ui.SelectExact("cmbLP", MainFlowInputs.Loadport, _main);
        _ui.AssertSelected("cmbLP", MainFlowInputs.Loadport, _main);
        context.Note("晶圆分布页已选择 Loadport1。");

        if (SkipHardware)
        {
            context.SkipHardwarePart("槽位识别状态与实物一致性未核对（跳过硬件策略）。");
            return;
        }

        context.Confirm(
            "核对晶圆分布页 Loadport1 槽1 的识别状态与实物一致：固定测试片在槽1；" +
            "如槽图未更新，先完成机台正常装盒扫描，不改数据库片位。完成后在网页选择“已完成”。");
    }

    /// <summary>
    /// 点击“任务”并选中“晶圆分布”页签，等待页签真正选中。
    /// 03.02 与 06.01 共用，不触发扫描或改变数据库片位。
    /// </summary>
    private void OpenWaferMapTab()
    {
        _ui.Invoke("Main.Job", _main);
        AutomationElement tab = _ui.Find("TabWaferMap", root: _main);
        SoftwareUi.RequirePattern(tab.Patterns.SelectionItem.IsSupported, "TabWaferMap", "SelectionItem");
        Verify.That(!tab.IsOffscreen, "晶圆分布页签未显示，环境与主流程不符。");
        tab.Patterns.SelectionItem.Pattern.Select();
        _ui.Wait(() => tab.Patterns.SelectionItem.Pattern.IsSelected.Value, "晶圆分布页签选中");
    }
}
