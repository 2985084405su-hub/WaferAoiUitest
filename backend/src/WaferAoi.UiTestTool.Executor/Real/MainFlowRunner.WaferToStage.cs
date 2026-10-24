using FlaUI.Core.AutomationElements;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 主流程 06：建档上片。端口/层号/读码方式/对准角选择为软件操作；
/// 一键上片与上片完成核对涉及机械手与运动平台，按本轮硬件策略分别自动执行、现场执行或跳过。
/// </summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>按文档顺序执行 06.01—06.05。</summary>
    private void RunWaferToStageSection()
    {
        Step("06.01", SelectLoadportLayer);
        if (_stoppedStatus != null) return;
        Step("06.02", _ => SelectReadCodeAndAngle());
        if (_stoppedStatus != null) return;
        Step("06.03", TransWaferToStage);
        if (_stoppedStatus != null) return;
        Step("06.04", WaitForWaferOnStage);
        if (_stoppedStatus != null) return;
        Step("06.05", AssertRecipeStillA);
    }

    /// <summary>06.01 任务→晶圆分布，选择 Loadport1 与层号 1，并回读当前显示。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SelectLoadportLayer(StepContext context)
    {
        OpenWaferMapTab();
        _ui.SelectExact("cmbLP", MainFlowInputs.Loadport, _main);
        _ui.AssertSelected("cmbLP", MainFlowInputs.Loadport, _main);
        _ui.SelectExact("cmbLPLayer", MainFlowInputs.LoadportLayer, _main);
        _ui.AssertSelected("cmbLPLayer", MainFlowInputs.LoadportLayer, _main);
        AssertNoMessageBox();

        if (SkipHardware)
        {
            context.SkipHardwarePart("Loadport1 层1 槽位识别状态与实物一致性未核对（跳过硬件策略）。");
            return;
        }

        context.Confirm(
            "核对晶圆分布页 Loadport1 层1 槽1 的识别状态与实物一致：固定测试片在槽1；" +
            "如槽图未更新，先完成机台正常装盒扫描，不改数据库片位。完成后在网页选择“已完成”。");
    }

    /// <summary>06.02 选择“关闭读码”和“对准角 0”，并回读。</summary>
    private void SelectReadCodeAndAngle()
    {
        _ui.SelectExact("cmbEnableScan", MainFlowInputs.ReadCodeMode, _main);
        _ui.AssertSelected("cmbEnableScan", MainFlowInputs.ReadCodeMode, _main);
        _ui.SelectExact("cmbAngle", MainFlowInputs.AlignmentAngle, _main);
        _ui.AssertSelected("cmbAngle", MainFlowInputs.AlignmentAngle, _main);
        AssertNoMessageBox();
    }

    /// <summary>
    /// 06.03 一键上片。
    /// 跳过策略只断言按钮并把动作记为跳过；人工策略由现场点击并在网页确认；
    /// 严格策略自动点击，按原用例处理介质/物料状态确认弹窗，不启动整片检测。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void TransWaferToStage(StepContext context)
    {
        AutomationElement button = _ui.Find("btnTransToStage", root: _main);
        Verify.That(!button.IsOffscreen, "一键上片按钮未显示，环境与主流程不符。");
        Verify.That(button.IsEnabled, "一键上片按钮当前不可用，请检查任务状态。");

        if (SkipHardware)
        {
            context.SkipWholeStep("上片动作涉及机械手与运动平台，本轮按跳过硬件策略未点击“一键上片到运动平台”。");
            return;
        }

        if (ManualHardware)
        {
            context.Confirm(
                "点击“一键上片到运动平台”，按本条注水、切割后状态逐项确认弹窗并等待开始上片；" +
                "不要启动整片检测。完成后在网页选择“已完成”。");
            _hardwareActuated = true;
            return;
        }

        _ui.Invoke("btnTransToStage", _main);
        _hardwareActuated = true;
        HandleUploadPrompts(context);
    }

    /// <summary>处理上片过程中出现的介质/物料状态确认弹窗；出现未知弹窗时阻塞。</summary>
    /// <param name="context">步骤上下文。</param>
    private void HandleUploadPrompts(StepContext context)
    {
        DateTimeOffset deadline = DateTimeOffset.Now + _settings.UiTimeout;
        int confirmed = 0;
        while (DateTimeOffset.Now < deadline)
        {
            AutomationElement? prompt = _ui.Optional("MessageBox.Content");
            if (prompt == null)
            {
                Task.Delay(250).GetAwaiter().GetResult();
                continue;
            }

            string text = prompt.Name ?? string.Empty;
            Verify.That(
                text.Contains("注水", StringComparison.Ordinal) ||
                text.Contains("切割后", StringComparison.Ordinal) ||
                text.Contains("物料", StringComparison.Ordinal) ||
                text.Contains("介质", StringComparison.Ordinal),
                "上片过程中出现非预期弹窗，不能自动确认：" + text);
            context.Note("已按本条固定条件确认上片提示：" + text);
            _ui.Invoke("MessageBox.OK");
            confirmed++;
            UiWait.Until(() => _ui.Optional("MessageBox.Content") == null, _settings.UiTimeout, "上片提示关闭");
            if (confirmed >= 5)
            {
                break;
            }
        }

        context.Note("已触发一键上片，本次确认弹窗数=" + confirmed + "。");
    }

    /// <summary>
    /// 06.04 等待上片完成并核对平台、来源槽位、机械手与报警。
    /// 严格策略只在能读到明确完成状态时通过；否则阻塞并说明缺少哪项可读来源。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void WaitForWaferOnStage(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("上片完成、平台/来源槽位/机械手/报警核对属于硬件状态核对，本轮按跳过硬件策略未执行。");
            return;
        }

        if (ManualHardware)
        {
            context.Confirm(
                "等待上片完成，核对：实片在运动平台、原 Loadport1 槽1 已空、机械手无遗留片、报警区无搬片报警。" +
                "完成后在网页选择“已完成”。");
            return;
        }

        // 严格策略：不接受“按钮已点”或固定延时代替完成状态。
        string? state = TryReadUploadCompletion();
        Verify.Available(
            state != null,
            "已触发一键上片，但界面上找不到可读的上片完成状态（未发现含“上片完成/搬运完成/已到平台”的可见文本）；" +
            "严格策略不能用“已点击”代替上片完成核对。请改用人工硬件策略，或提供该状态的可读来源。");
        Verify.That(!HasMessageBox(), "上片完成后仍存在未处理弹窗，不能判定通过。");
        context.Note("上片完成状态：" + state);
    }

    /// <summary>
    /// 在上片缓冲时间内轮询界面文本，寻找可读的上片完成状态。
    /// </summary>
    /// <returns>命中的状态文本；找不到时为 null。</returns>
    private string? TryReadUploadCompletion()
    {
        string[] keywords = { "上片完成", "搬运完成", "已到平台", "上片结束" };
        DateTimeOffset deadline = DateTimeOffset.Now + _settings.HardwareActionTimeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (HasMessageBox())
            {
                return null;
            }

            foreach (Window window in _application!.GetTopLevelWindows())
            {
                AutomationElement[] descendants;
                try
                {
                    descendants = window.FindAllDescendants();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (AutomationElement element in descendants)
                {
                    string name;
                    try
                    {
                        name = element.Name ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (string keyword in keywords)
                    {
                        if (name.Contains(keyword, StringComparison.Ordinal))
                        {
                            return name;
                        }
                    }
                }
            }

            Task.Delay(500).GetAwaiter().GetResult();
        }

        return null;
    }

    /// <summary>06.05 点击“配方”，确认当前编辑配方仍是本轮 A。</summary>
    /// <param name="context">步骤上下文。</param>
    private void AssertRecipeStillA(StepContext context)
    {
        _ui.Invoke("Main.Recipe", _main);
        string fileName = _recipeAName + ".rcp";
        _ui.Wait(
            () => string.Equals(_recipeEditor.ReadCurrentRecipeName(), fileName, StringComparison.Ordinal),
            "点击配方后当前编辑配方仍为 " + fileName);
        AssertNoMessageBox();
        context.Note("当前编辑配方为 " + fileName + "，后续图像采集写入 A。");
    }
}
