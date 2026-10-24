using System.Globalization;
using FlaUI.Core.AutomationElements;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 主流程 07：切割道两点角度校正（07.01—07.18）。
///
/// 成功标准严格按原用例：必须由被测程序实际完成切割道角度校正并返回“运行成功”，
/// 角度偏移/纠正后角度由算法输出。保存成功、仅出现调试图或初始角度为 0 都不算校正成功；
/// 也不向结果框手填任何值。
/// </summary>
internal sealed partial class MainFlowRunner
{
    /// <summary>按文档顺序执行 07.01—07.18。</summary>
    private void RunStreetAlignmentSection()
    {
        Step("07.01", OpenStreetAlignment);
        if (_stoppedStatus != null) return;
        Step("07.02", context => SelectAngleCamera(context));
        if (_stoppedStatus != null) return;
        Step("07.03", context => SelectAngleMagnification(context));
        if (_stoppedStatus != null) return;
        Step("07.04", ConfigureAngleLighting);
        if (_stoppedStatus != null) return;
        Step("07.05", ConfigureAngleExposure);
        if (_stoppedStatus != null) return;
        Step("07.06", _ => _street.SelectNewAlignmentAndAfterCut());
        if (_stoppedStatus != null) return;
        Step("07.07", context => SetAngleParameter(context, "低阈值", MainFlowInputs.LowThreshold));
        if (_stoppedStatus != null) return;
        Step("07.08", context => SetAngleParameter(context, "Die高度", MainFlowInputs.DieHeight));
        if (_stoppedStatus != null) return;
        Step("07.09", SetAngleParameterPair);
        if (_stoppedStatus != null) return;
        Step("07.10", StartAngleContinuousGrab);
        if (_stoppedStatus != null) return;
        Step("07.11", FocusAndGrabSingle);
        if (_stoppedStatus != null) return;
        Step("07.12", AddFirstPoint);
        if (_stoppedStatus != null) return;
        Step("07.13", MoveXTwentyMillimeters);
        if (_stoppedStatus != null) return;
        Step("07.14", AddSecondPoint);
        if (_stoppedStatus != null) return;
        Step("07.15", SaveAngleCalibration);
        if (_stoppedStatus != null) return;
        Step("07.16", MoveToFirstPointAndDebug);
        if (_stoppedStatus != null) return;
        Step("07.17", RunAngleCalibration);
        if (_stoppedStatus != null) return;
        Step("07.18", ReadAngleResults);
    }

    /// <summary>07.01 进入角度校正页，核对当前配方为 A、点位列表与“增加/采图调试”存在、四参数可见。</summary>
    /// <param name="context">步骤上下文。</param>
    private void OpenStreetAlignment(StepContext context)
    {
        _street = new StreetAlignmentPage(_main, _ui, _settings.UiTimeout, _artifacts.Log);
        _street.Open();

        string fileName = _recipeAName + ".rcp";
        _ui.Wait(
            () => string.Equals(_recipeEditor.ReadCurrentRecipeName(), fileName, StringComparison.Ordinal),
            "角度校正页当前配方仍为 " + fileName);
        foreach (string label in new[] { "低阈值", "Die高度", "黑白切割道", "粗校正系数" })
        {
            Verify.Available(
                NamedUi.FindRowEdit(_main, label) != null,
                "角度校正页未能唯一定位参数“" + label + "”的输入框（" + NamedUi.ExplainRowEdit(_main, label) +
                "）；只有确认页面上确实没有该参数时，才说明当前页面分支与用例不符。");
        }

        _artifacts.CaptureScreen("07.01角度校正页", "07.01");
        context.Note("当前配方=" + fileName + "；已确认点位列表区域、“增加”“采图调试”和四个算法参数。");
    }

    /// <summary>07.02 选择 InspectCamera 并核对选中状态。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SelectAngleCamera(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("相机与运动/光学操作属于硬件设置，本轮按跳过硬件策略未执行。");
            return;
        }

        _street.SelectCamera(MainFlowInputs.AngleCamera);
        AssertNoMessageBox();
        context.Note("相机=" + MainFlowInputs.AngleCamera + "；运动与光学操作未报硬件错误。");
    }

    /// <summary>07.03 选择 5X 并点击“设置”。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SelectAngleMagnification(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("倍率切换属于硬件设置，本轮按跳过硬件策略未执行。");
            return;
        }

        _street.SelectMagnification(MainFlowInputs.AngleMagnification);
        AssertNoMessageBox();
        context.Note("倍率=" + MainFlowInputs.AngleMagnification + "，已点击“设置”，无失败或未标定提示。");
    }

    /// <summary>07.04 明场 300 并设置；关闭红外与可见暗场。</summary>
    /// <param name="context">步骤上下文。</param>
    private void ConfigureAngleLighting(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("光源设置属于硬件操作，本轮按跳过硬件策略未执行。");
            return;
        }

        IReadOnlyList<string> handled = _street.ConfigureLighting(MainFlowInputs.BrightFieldValue);
        AssertNoMessageBox();
        foreach (string item in handled)
        {
            context.Note(item);
        }
    }

    /// <summary>07.05 处理可见曝光/增益字段；未显示字段如实记录。</summary>
    /// <param name="context">步骤上下文。</param>
    private void ConfigureAngleExposure(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("相机曝光/增益设置属于硬件操作，本轮按跳过硬件策略未执行。");
            return;
        }

        IReadOnlyList<string> results = _street.ConfigureExposureAndGain(
            MainFlowInputs.ExposureMicroseconds,
            MainFlowInputs.Gain);
        AssertNoMessageBox();
        foreach (string item in results)
        {
            context.Note(item);
        }

        bool anyApplied = results.Any(item => !item.Contains("未显示", StringComparison.Ordinal));
        if (!anyApplied)
        {
            // 用例对“未显示”的要求是如实记录、不记为已设置；该版本角度校正页把三项都隐藏了，记录后继续，
            // 由后续真实采图与运行成功证明取图链路可用。
            context.Note("曝光时间/硬件增益/逻辑增益在本版本角度校正页均未显示，按用例记录为“未显示”，不记为已设置。");
        }
    }

    /// <summary>07.07／07.08 写入单个算法参数并回读。</summary>
    /// <param name="context">步骤上下文。</param>
    /// <param name="label">标签。</param>
    /// <param name="value">目标值。</param>
    private void SetAngleParameter(StepContext context, string label, string value)
    {
        _street.SetAlgorithmParameter(label, value);
        AssertNoMessageBox();
        context.Note(label + "=" + _street.ReadAlgorithmParameter(label));
    }

    /// <summary>07.09 黑白切割道填 1、粗校正系数填 1 并分别回读。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SetAngleParameterPair(StepContext context)
    {
        _street.SetAlgorithmParameter("黑白切割道", MainFlowInputs.BlackWhiteStreet);
        _street.SetAlgorithmParameter("粗校正系数", MainFlowInputs.CoarseCorrection);
        AssertNoMessageBox();
        Verify.Equal(MainFlowInputs.BlackWhiteStreet, _street.ReadAlgorithmParameter("黑白切割道"), "黑白切割道回读不一致。");
        Verify.Equal(MainFlowInputs.CoarseCorrection, _street.ReadAlgorithmParameter("粗校正系数"), "粗校正系数回读不一致。");
        context.Note("黑白切割道=1（黑），粗校正系数=1。");
    }

    /// <summary>
    /// 07.10 点击“连续采集”，切到寸动并把 XY 步距设为 0.100，由现场把视野移到水平黑切割道。
    /// 严格策略不能替代肉眼取景，如实阻塞并说明原因。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void StartAngleContinuousGrab(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("取景与连续采集属于硬件操作，本轮按跳过硬件策略未执行。");
            return;
        }

        if (StrictHardware)
        {
            throw Verify.Blocked(
                "严格策略无法自动完成 07.10 的取景：原用例要求用肉眼确认“所选行无墨点、晶粒正常、" +
                "向 X 正方向 20mm 范围均在片内”，生产界面没有可读的图像内容或轴行程判据，脚本不能代替现场判断。" +
                "请使用“人工执行硬件步骤”策略完成本节。");
        }

        _street.StartContinuousGrab();
        _street.SelectJogTab();
        _street.SetJogStep(MainFlowInputs.JogStepXy);
        context.Note("已点击连续采集；寸动步距=" + _street.ReadJogStep() + "。");

        context.Confirm(
            "图像持续刷新中。用寸动把视野移到片中央附近一条水平黑切割道：确认画面能看见切割道及相邻 Die 边缘，" +
            "所选行无墨点、晶粒正常，且向 X 正方向 20mm 的范围都在片内、轴未接近限位。完成后在网页选择“已完成”。");
        _hardwareActuated = true;

        Verify.Equal(MainFlowInputs.JogStepXy, _street.ReadJogStep(), "取景后寸动步距应保持 0.100。");
        AssertImageHasContent(context, "07.10", requireContrast: true);
    }

    /// <summary>
    /// 07.11 Z 调焦：步距改 0.010，由现场用 Z 按钮调焦；随后脚本恢复 0.100、停止连续采集并采单张。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void FocusAndGrabSingle(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("调焦与采集操作属于硬件动作，本轮按跳过硬件策略未执行。");
            return;
        }

        if (StrictHardware)
        {
            throw Verify.Blocked(
                "严格策略无法自动完成 07.11 的调焦：原用例要求用 Z 方向按钮目视调到切割道与 Die 边缘清晰，" +
                "生产界面没有可读的清晰度判据，脚本不能用固定步数或“无异常”代替对焦。" +
                "请使用“人工执行硬件步骤”策略完成本节。");
        }

        _street.SetJogStep(MainFlowInputs.JogStepFine);
        context.Confirm(
            "步距已改为 0.010。用标为 Z 的上下方向按钮逐步调焦，直到切割道和 Die 边缘清晰、图像非全黑/全白；" +
            "调好后不要动 Theta，在网页选择“已完成”，脚本随后恢复步距 0.100、停止连续采集并采单张。");
        _hardwareActuated = true;

        _street.SetJogStep(MainFlowInputs.JogStepXy);
        _street.StopContinuousGrab();
        _street.GrabSingle();
        AssertNoMessageBox();
        Verify.Equal(MainFlowInputs.JogStepXy, _street.ReadJogStep(), "调焦后步距应恢复 0.100。");

        Axes axes = _street.ReadAxes();
        context.Note(string.Format(
            CultureInfo.InvariantCulture,
            "当前轴位 X={0:0.0000} Y={1:0.0000} Z={2:0.0000} Theta={3:0.0000}；后续不再手动改变 Z/Theta。",
            axes.X, axes.Y, axes.Z, axes.Theta));
        _artifacts.CaptureScreen("07.11调焦后取景", "07.11");
        AssertImageHasContent(context, "07.11", requireContrast: true);
    }

    /// <summary>07.12 核对点位列表为空，点“增加”一次，并核对行值与当前轴位一致（4 位小数）。</summary>
    /// <param name="context">步骤上下文。</param>
    private void AddFirstPoint(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("点位采集依赖实际轴位，本轮按跳过硬件策略未执行。");
            return;
        }

        int before = TryPointRowCount(out bool gridFound);
        Verify.Available(gridFound, "未能在角度页定位点位列表控件，无法核对“新建配方点位列表为空”。");
        Verify.Equal(0, before, "新建配方点位列表应为空；若已存在旧点，本步骤失败，不直接追加。");

        _street.AddPoint();
        AssertNoMessageBox();
        _ui.Wait(() => TryPointRowCount(out _) == 1, "点位列表出现 1 行");

        IReadOnlyList<(string X, string Y)> rows = _street.ReadPointRows();
        _point1 = _street.ReadAxes();
        string expectedX = Format4(_point1.X);
        string expectedY = Format4(_point1.Y);
        Verify.Equal(1, rows.Count, "增加一次后点位列表应恰有 1 行。");
        Verify.That(
            SameRounded(rows[0].X, expectedX) && SameRounded(rows[0].Y, expectedY),
            $"第 1 点行值({rows[0].X}/{rows[0].Y})与当前轴位({expectedX}/{expectedY})不一致。");
        context.Note($"X1={rows[0].X}，Y1={rows[0].Y}（与当前轴位四舍五入到 4 位小数一致）。");
    }

    /// <summary>
    /// 07.13 步距改 1.000，逐次点击 X 左箭头 20 次，核对 X 增加约 20mm 且 Y 不变。
    /// </summary>
    /// <param name="context">步骤上下文。</param>
    private void MoveXTwentyMillimeters(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("轴运动属于硬件动作，本轮按跳过硬件策略未执行。");
            return;
        }

        if (ManualHardware)
        {
            context.Confirm(
                "在寸动“步距”填 1.000 并离焦，逐次点击 X 轴左侧方向按钮共 20 次（每次等运动停稳再点下一次），" +
                "不要点 Y 方向按钮；确认 X 读数相对 X1 增加约 20.0000mm、Y 保持不变。完成后在网页选择“已完成”。");
            _hardwareActuated = true;
        }
        else
        {
            _street.SetJogStep(MainFlowInputs.JogStepGap);
            AxisSnapshot snapshot = _street.ClickPositiveX(MainFlowInputs.JogGapClicks);
            double delta = snapshot.XAfterAll - snapshot.XBefore;
            Verify.That(
                Math.Abs(delta - MainFlowInputs.ExpectedDeltaX) <= MainFlowInputs.DeltaXTolerance,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "X 实际位移 {0:0.0000}mm 与期望 {1:0.0000}mm 不符（允许误差 ±{2:0.0000}mm）。",
                    delta, MainFlowInputs.ExpectedDeltaX, MainFlowInputs.DeltaXTolerance));
            Verify.That(
                Math.Abs(snapshot.YAfter - snapshot.YBefore) < 0.0001,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Y 不应变化，实际 {0:0.0000} → {1:0.0000}。",
                    snapshot.YBefore, snapshot.YAfter));
            context.Note(string.Format(
                CultureInfo.InvariantCulture,
                "步距 1.000 点击 20 次；X {0:0.0000} → {1:0.0000}（Δ={2:0.0000}mm），Y 保持 {3:0.0000}。",
                snapshot.XBefore, snapshot.XAfterAll, delta, snapshot.YAfter));
            _hardwareActuated = true;
        }

        AssertNoMessageBox();
        Verify.That(!HasMessageBox(), "轴运动期间出现限位或运动错误弹窗。");
    }

    /// <summary>07.14 采单张并增加第二点，核对列表 2 行、X2≈X1+20、Y2=Y1，最后恢复步距 0.100。</summary>
    /// <param name="context">步骤上下文。</param>
    private void AddSecondPoint(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("第二点采集依赖实际轴位与图像，本轮按跳过硬件策略未执行。");
            return;
        }

        if (StrictHardware)
        {
            _street.GrabSingle();
            AssertImageHasContent(context, "07.14", requireContrast: true);
        }
        else
        {
            _street.GrabSingle();
            AssertImageHasContent(context, "07.14", requireContrast: true);
        }

        Verify.That(!HasMessageBox(), "采单张后出现错误弹窗，不能继续记录第二点。");
        _street.AddPoint();
        AssertNoMessageBox();
        _ui.Wait(() => TryPointRowCount(out _) == 2, "点位列表出现 2 行");
        _street.SetJogStep(MainFlowInputs.JogStepXy);

        IReadOnlyList<(string X, string Y)> rows = _street.ReadPointRows();
        _point2 = _street.ReadAxes();
        Verify.Equal(2, rows.Count, "增加第二点后点位列表应恰有 2 行。");

        double x1 = ParseNumber(rows[0].X);
        double x2 = ParseNumber(rows[1].X);
        double y1 = ParseNumber(rows[0].Y);
        double y2 = ParseNumber(rows[1].Y);
        Verify.That(
            Math.Abs(x2 - x1 - MainFlowInputs.ExpectedDeltaX) <= MainFlowInputs.DeltaXTolerance,
            string.Format(
                CultureInfo.InvariantCulture,
                "X2({0:0.0000}) 与 X1＋20mm({1:0.0000}) 不符。",
                x2, x1 + MainFlowInputs.ExpectedDeltaX));
        Verify.That(
            Math.Abs(y2 - y1) < 0.0001,
            string.Format(CultureInfo.InvariantCulture, "两点必须同 Y：Y1={0:0.0000}，Y2={1:0.0000}。", y1, y2));
        Verify.Equal(MainFlowInputs.JogStepXy, _street.ReadJogStep(), "第二点采集后步距应恢复 0.100。");
        context.Note($"X1/Y1={rows[0].X}/{rows[0].Y}；X2/Y2={rows[1].X}/{rows[1].Y}；步距已恢复 0.100。");
    }

    /// <summary>07.15 保存并确认无“点位过少”或“保存失败”提示，列表与四参数保持。</summary>
    /// <param name="context">步骤上下文。</param>
    private void SaveAngleCalibration(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("保存会把当前实际 Z/Theta 与点位写入配方，本轮点位未采集，按跳过硬件策略未执行保存。");
            return;
        }

        _street.Save();
        UiWait.Until(
            () =>
            {
                AutomationElement? prompt = _ui.Optional("MessageBox.Content");
                if (prompt == null)
                {
                    return true;
                }

                string text = prompt.Name ?? string.Empty;
                Verify.That(
                    !text.Contains("点位过少", StringComparison.Ordinal) &&
                    !text.Contains("保存失败", StringComparison.Ordinal),
                    "保存角度校正失败：" + text);
                // 其他提示同样不能静默确认，交由现场判断。
                throw Verify.Blocked("保存后出现提示，需现场确认后再继续：" + text);
            },
            _settings.UiTimeout,
            "保存后无失败提示");
        AssertNoMessageBox();

        Verify.Equal(2, TryPointRowCount(out _), "保存后点位列表仍应为 2 点。");
        Verify.Equal(MainFlowInputs.LowThreshold, _street.ReadAlgorithmParameter("低阈值"), "保存后低阈值应仍为 50。");
        Verify.Equal(MainFlowInputs.DieHeight, _street.ReadAlgorithmParameter("Die高度"), "保存后 Die 高度应仍为 500。");
        Verify.Equal(MainFlowInputs.BlackWhiteStreet, _street.ReadAlgorithmParameter("黑白切割道"), "保存后黑白切割道应仍为 1。");
        Verify.Equal(MainFlowInputs.CoarseCorrection, _street.ReadAlgorithmParameter("粗校正系数"), "保存后粗校正系数应仍为 1。");
        context.Note("保存无点位过少/保存失败提示；列表仍为 2 点，四参数仍为 50/500/1/1。");

        _ui.Wait(
            () => string.Equals(_recipeEditor.ReadCurrentRecipeName(), _recipeAName + ".rcp", StringComparison.Ordinal),
            "保存后当前配方仍为 A");
    }

    /// <summary>07.16 移动回第一点并采图调试，核对无调试算法报错且图像区有内容。</summary>
    /// <param name="context">步骤上下文。</param>
    private void MoveToFirstPointAndDebug(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("点位移动与采图调试属于硬件动作，本轮按跳过硬件策略未执行。");
            return;
        }

        _street.MoveToPoint(0);
        Axes axes = _street.ReadAxes();
        Verify.That(
            Math.Abs(axes.X - _point1.X) < 0.01 && Math.Abs(axes.Y - _point1.Y) < 0.01,
            string.Format(
                CultureInfo.InvariantCulture,
                "移动后轴位({0:0.0000},{1:0.0000})未回到第一点({2:0.0000},{3:0.0000})。",
                axes.X, axes.Y, _point1.X, _point1.Y));
        _hardwareActuated = true;

        _street.DebugCapture();
        // 调试图是 Cognex 内部图像，UIA 读不到像素；此处以“无错误弹窗 + 图像区非空白统计”作为客观依据，
        // 算法正确性由 07.17 的运行成功提示证明，不能用本步骤代替。
        UiWait.Until(
            () =>
            {
                AutomationElement? prompt = _ui.Optional("MessageBox.Content");
                if (prompt == null)
                {
                    return true;
                }

                string text = prompt.Name ?? string.Empty;
                Verify.That(
                    !text.Contains("调试算法报错", StringComparison.Ordinal) &&
                    !text.Contains("采图失败", StringComparison.Ordinal) &&
                    !text.Contains("硬件错误", StringComparison.Ordinal),
                    "采图调试失败：" + text);
                throw Verify.Blocked("采图调试出现提示，需现场确认：" + text);
            },
            _settings.HardwareActionTimeout,
            "采图调试无错误提示");

        AssertImageHasContent(context, "07.16", requireContrast: true);
        _artifacts.CaptureScreen("07.16调试图", "07.16");
        context.Note("轴已回到第一点并采图调试；无调试算法报错，调试图区域非空白。");
    }

    /// <summary>07.17 运行角度校正并确认“运行成功!”提示。</summary>
    /// <param name="context">步骤上下文。</param>
    private void RunAngleCalibration(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("角度校正运行会驱动相机与轴，本轮按跳过硬件策略未执行。");
            return;
        }

        if (ManualHardware)
        {
            context.Confirm(
                "点击页面底部公共“运行”，等待弹窗；确认提示为“运行成功!”后点击“确定”。" +
                "没有粗/精校准失败、飞拍超时、限位、标定或相机错误。完成后在网页选择“已完成”。");
            _hardwareActuated = true;
            return;
        }

        string prompt = _street.RunAndReadPrompt(_settings.HardwareActionTimeout);
        _artifacts.WriteText("07.17运行提示", prompt, "07.17", "text");
        Verify.That(
            prompt.Contains(MainFlowInputs.RunSuccessKeyword, StringComparison.Ordinal),
            "角度校正未返回成功提示，实际弹窗为：" + prompt);
        _street.ConfirmPrompt();
        UiWait.Until(() => !_street.HasMessageBox(), _settings.UiTimeout, "运行成功提示已确认");
        _hardwareActuated = true;
        context.Note("被测程序返回“" + prompt.Trim() + "”。");
    }

    /// <summary>07.18 读取角度偏移与纠正后角度，截图并保留运行日志。</summary>
    /// <param name="context">步骤上下文。</param>
    private void ReadAngleResults(StepContext context)
    {
        if (SkipHardware)
        {
            context.SkipWholeStep("角度结果由实际校正运行产生，本轮未运行校正，按跳过硬件策略不读取结果。");
            return;
        }

        if (ManualHardware)
        {
            context.Confirm(
                "读取页面上的“角度偏移”“纠正后角度”并截图保存；两个值都必须是有限数值（允许为 0），" +
                "不要向这两个框手填 0。完成后在网页选择“已完成”。");
        }

        string offset = _street.ReadAngleOffset();
        string corrected = _street.ReadCorrectedAngle();
        Verify.Available(
            offset.Length > 0 && corrected.Length > 0,
            "未读到“角度偏移”或“纠正后角度”结果框的内容；不能用人工填写值代替算法输出。");
        Verify.That(IsFiniteNumber(offset), "角度偏移不是有限数值：" + offset);
        Verify.That(IsFiniteNumber(corrected), "纠正后角度不是有限数值：" + corrected);

        _artifacts.CaptureScreen("07.18角度结果", "07.18");
        _artifacts.WriteText(
            "07.18角度结果",
            "角度偏移=" + offset + Environment.NewLine + "纠正后角度=" + corrected + Environment.NewLine,
            "07.18",
            "text");
        _artifacts.RegisterStepLog("07.18");
        context.Note("角度偏移=" + offset + "；纠正后角度=" + corrected + "（保持算法输出，未手填）。");
    }

    /// <summary>读取图像区并核对非全黑/全白；读不到图像统计时记录事实但不伪造通过。</summary>
    /// <param name="context">步骤上下文。</param>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="requireContrast">是否要求存在对比度。</param>
    private void AssertImageHasContent(StepContext context, string stepId, bool requireContrast)
    {
        if (!_street.TryMeasureImageView(out ImageStatistics statistics))
        {
            throw Verify.Blocked(
                "未能截取图像区做亮度统计，无法客观判断“画面非全黑/全白”；请提供图像宿主控件或改人工策略。");
        }

        _artifacts.WriteText(
            stepId + "图像统计",
            string.Format(
                CultureInfo.InvariantCulture,
                "samples={0}; mean={1:0.0}; min={2}; max={3}; contrast={4}",
                statistics.Samples, statistics.Mean, statistics.Min, statistics.Max, statistics.Contrast),
            stepId,
            "metrics");
        Verify.That(statistics.Samples > 0, "图像区没有可采样像素。");
        Verify.That(
            statistics.Mean > 2 && statistics.Mean < 253,
            string.Format(CultureInfo.InvariantCulture, "图像平均亮度 {0:0.0} 接近全黑或全白。", statistics.Mean));
        if (requireContrast)
        {
            Verify.That(
                statistics.Contrast >= 12,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "图像对比度仅 {0}（最大{1}/最小{2}），画面缺少可辨识内容。",
                    statistics.Contrast, statistics.Max, statistics.Min));
        }

        context.Note(string.Format(
            CultureInfo.InvariantCulture,
            "图像统计：mean={0:0.0}，对比度={1}（截图统计，不等同算法识别成功）。",
            statistics.Mean, statistics.Contrast));
    }

    /// <summary>读取点位行数；列表不存在时返回 0 并标记未找到。</summary>
    /// <param name="gridFound">是否找到列表控件。</param>
    /// <returns>行数。</returns>
    private int TryPointRowCount(out bool gridFound)
    {
        try
        {
            gridFound = true;
            return _street.PointRowCount();
        }
        catch (StepVerdictException)
        {
            gridFound = false;
            return 0;
        }
    }

    /// <summary>按 4 位小数格式化。</summary>
    /// <param name="value">轴位值。</param>
    /// <returns>文本。</returns>
    private static string Format4(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>比较两个数字文本四舍五入到 4 位后是否一致。</summary>
    /// <param name="actual">实际显示值。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>一致时为 true。</returns>
    private static bool SameRounded(string actual, string expected)
    {
        if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double left) ||
            !double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double right))
        {
            return false;
        }

        return Math.Round(left, 4) == Math.Round(right, 4);
    }

    /// <summary>解析界面数字。</summary>
    /// <param name="text">文本。</param>
    /// <returns>数值；无法解析时以阻塞结束步骤。</returns>
    private static double ParseNumber(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        throw Verify.Blocked("点位列表中的数值无法解析：'" + text + "'。");
    }

    /// <summary>是否为有限数值。</summary>
    /// <param name="text">文本。</param>
    /// <returns>有限数值为 true。</returns>
    private static bool IsFiniteNumber(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }
}
