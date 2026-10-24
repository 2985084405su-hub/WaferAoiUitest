using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 07 切割道两点角度校正页的实际 UIA 操作与观测。
///
/// 该页绝大多数业务控件（新/旧转正、切割前/后、四个算法参数、点位列表、寸动按钮、
/// 角度结果框）在生产源码中没有 AutomationId（见用例第07节截图核对依据）。
/// 因此本类不使用截图坐标，而是按下列可复现规则在 UIA 树上定位：
/// 1) 页面导航与保存/运行使用生产已公开的 AutomationId（RecipeStep.*）。
/// 2) 有 WPF Name 回退 ID 的控件直接用 ID（cb_camera/tbxExposureTime/tbxGain/tbxProgramGain/
///    ucBrightTxt/ucInfraredTxt/grabLive/grabOne）。
/// 3) 无 ID 控件按“标签文字 + 控件类型 + 同一行的几何归属”定位：先唯一定位标签，
///    再取同一行内（垂直中心重合）最近的指定类型控件；不按绝对坐标、不按序号猜控件。
/// 4) 任何一步无法唯一定位时，本步骤阻塞并写出页面 UIA 树证据，绝不猜测或伪造通过。
/// </summary>
internal sealed class StreetAlignmentPage
{
    /// <summary>角度校正导航项 AutomationId。</summary>
    public const string NavigationId = "RecipeStep.WaferThetaAlignment";

    /// <summary>图像宿主候选 AutomationId（Cognex 宿主，用于截图统计而非像素内容断言）。</summary>
    private static readonly string[] ImageHostIds = { "VPImage", "camVpGrab", "imgVP", "imageView" };

    /// <summary>主窗口。</summary>
    private readonly Window _main;

    /// <summary>绑定本轮进程的 UI 操作器。</summary>
    private readonly OwnedUi _ui;

    /// <summary>默认超时。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>日志。</summary>
    private readonly Action<string> _log;

    /// <summary>创建角度页对象。</summary>
    /// <param name="main">主窗口。</param>
    /// <param name="ui">UI 操作器。</param>
    /// <param name="timeout">默认超时。</param>
    /// <param name="log">日志。</param>
    public StreetAlignmentPage(Window main, OwnedUi ui, TimeSpan timeout, Action<string> log)
    {
        _main = main;
        _ui = ui;
        _timeout = timeout;
        _log = log;
    }

    /// <summary>07.01 打开角度校正页并等待点位列表、增加、采图调试出现。</summary>
    public void Open()
    {
        _ui.Invoke(NavigationId, _main);
        UiWait.UntilNotNull(() => NamedUi.FindButton(_main, "增加"), _timeout, "角度校正页“增加”按钮出现");
        UiWait.UntilNotNull(() => NamedUi.FindButton(_main, "采图调试"), _timeout, "角度校正页“采图调试”按钮出现");
        _log("已进入切割道两点角度校正页。");
    }

    /// <summary>07.02 选择相机。</summary>
    /// <param name="camera">相机名，例如 InspectCamera。</param>
    public void SelectCamera(string camera)
    {
        ComboBox combo = _ui.Find("cb_camera", _timeout, _main).AsComboBox();
        SelectComboBoxItemByText(combo, camera, "相机");
        _log("相机已选择：" + camera);
    }

    /// <summary>
    /// 07.03 选择“主复检转塔”倍率并点击其旁边的“设置”。
    /// 倍率框没有 ID：以“包含指定倍率选项的组合框”唯一定位，不按界面顺序取第一个。
    /// </summary>
    /// <param name="magnification">倍率文字，例如 5X。</param>
    public void SelectMagnification(string magnification)
    {
        ComboBox? target = null;
        foreach (AutomationElement candidate in _main.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox)))
        {
            string id = SafeId(candidate);
            if (string.Equals(id, "cb_camera", StringComparison.Ordinal))
            {
                continue;
            }

            if (ComboContains(candidate, magnification))
            {
                if (target != null)
                {
                    throw Verify.Blocked("页面上有多个包含倍率选项 '" + magnification + "' 的组合框，无法唯一定位倍率框。");
                }

                target = candidate.AsComboBox();
            }
        }

        Verify.Available(target != null, "无法定位包含倍率选项 '" + magnification + "' 的组合框（主复检转塔倍率框）。");
        SelectComboBoxItemByText(target!, magnification, "倍率");

        AutomationElement button = NamedUi.FindNearestButton(_main, target!, "设置")
            ?? throw Verify.Blocked("未能在倍率框同一行定位“设置”按钮。");
        Click(button, "倍率设置");
        _log("倍率已选择并设置：" + magnification);
    }

    /// <summary>
    /// 07.04 勾选明场照明、写入值并点击该行“设置”；取消红外（及可见暗场）并点击“关闭”。
    /// </summary>
    /// <param name="brightValue">明场照明值。</param>
    /// <returns>实际处理的照明行名称，供证据记录。</returns>
    public IReadOnlyList<string> ConfigureLighting(string brightValue)
    {
        var handled = new List<string>();

        CheckBox? bright = NamedUi.FindCheckBoxContaining(_main, "明场");
        Verify.Available(bright != null, "未能在 UIA 树中定位“明场照明”勾选框。");
        if (bright!.IsChecked != true)
        {
            bright.IsChecked = true;
        }

        WaitChecked(bright, true, "明场照明已勾选");
        _ui.SetValue("ucBrightTxt", brightValue, root: _main);
        AutomationElement brightSet = NamedUi.FindNearestButton(_main, bright, "设置")
            ?? throw Verify.Blocked("未能在明场照明同一行定位“设置”按钮。");
        Click(brightSet, "明场设置");
        handled.Add("明场照明=" + brightValue);

        handled.Add(DisableLightingRow("红外", "红外照明"));

        CheckBox? dark = NamedUi.FindCheckBoxContaining(_main, "暗场");
        if (dark != null)
        {
            handled.Add(DisableLightingRow("暗场", "暗场照明"));
        }
        else
        {
            handled.Add("暗场行未显示（按用例无需寻找暗场控件）");
        }

        return handled;
    }

    /// <summary>取消一行照明勾选并点击该行“关闭”。</summary>
    /// <param name="keyword">勾选框关键字。</param>
    /// <param name="displayName">证据中的名称。</param>
    /// <returns>处理结果说明。</returns>
    private string DisableLightingRow(string keyword, string displayName)
    {
        CheckBox? checkBox = NamedUi.FindCheckBoxContaining(_main, keyword);
        Verify.Available(checkBox != null, "未能在 UIA 树中定位“" + displayName + "”勾选框。");
        if (checkBox!.IsChecked == true)
        {
            checkBox.IsChecked = false;
        }

        WaitChecked(checkBox, false, displayName + "已取消勾选");
        AutomationElement? close = NamedUi.FindNearestButton(_main, checkBox, "关闭");
        if (close != null)
        {
            Click(close, displayName + "关闭");
            return displayName + "=未勾选并已关闭";
        }

        // 部分版本该行没有“关闭”按钮；此时只记录可观察事实，不推断硬件已关闭。
        return displayName + "=未勾选（该行未提供“关闭”按钮）";
    }

    /// <summary>
    /// 07.05 处理可见的曝光/增益字段；不可见的字段如实记为未显示。
    /// </summary>
    /// <param name="exposure">曝光值。</param>
    /// <param name="gain">增益值。</param>
    /// <returns>逐字段处理结果。</returns>
    public IReadOnlyList<string> ConfigureExposureAndGain(string exposure, string gain)
    {
        var results = new List<string>();
        foreach ((string id, string value, string name) in new[]
                 {
                     ("tbxExposureTime", exposure, "曝光时间"),
                     ("tbxGain", gain, "硬件增益"),
                     ("tbxProgramGain", gain, "逻辑增益")
                 })
        {
            AutomationElement? element = _main.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element == null || element.IsOffscreen)
            {
                results.Add(name + "=未显示(未设置为" + value + ")");
                continue;
            }

            _ui.SetValue(id, value, root: _main);
            results.Add(name + "=" + value);
        }

        return results;
    }

    /// <summary>07.06 选择“新转正方式”和“切割后”，并核对互斥项未被选中。</summary>
    public void SelectNewAlignmentAndAfterCut()
    {
        SelectRadio("新转正方式");
        SelectRadio("切割后");
        Verify.That(!IsRadioSelected("旧转正方式"), "旧转正方式不应被选中。");
        Verify.That(!IsRadioSelected("切割前"), "切割前不应被选中。");
        _log("已选择新转正方式 + 切割后。");
    }

    /// <summary>选中名称精确等于指定文字的 RadioButton。</summary>
    /// <param name="name">单选按钮文字。</param>
    private void SelectRadio(string name)
    {
        RadioButton? radio = NamedUi.FindRadio(_main, name)
            ?? throw Verify.Blocked("未能在 UIA 树中唯一定位单选按钮“" + name + "”。");
        Verify.That(radio.IsEnabled, "单选按钮“" + name + "”当前不可用。");
        radio.IsChecked = true;
        UiWait.Until(() => radio.IsChecked == true, _timeout, "单选按钮“" + name + "”已选中");
    }

    /// <summary>查询单选按钮是否选中；按钮不存在时返回 false。</summary>
    /// <param name="name">按钮文字。</param>
    /// <returns>选中状态。</returns>
    private bool IsRadioSelected(string name)
    {
        RadioButton? radio = NamedUi.FindRadio(_main, name);
        return radio?.IsChecked == true;
    }

    /// <summary>
    /// 07.07—07.09 写入四个算法参数：按标签定位同一行的输入框，写入后失焦并回读。
    /// </summary>
    /// <param name="label">标签文字，例如“低阈值”。</param>
    /// <param name="value">目标值。</param>
    public void SetAlgorithmParameter(string label, string value)
    {
        AutomationElement edit = NamedUi.FindRowEdit(_main, label)
            ?? throw Verify.Blocked("未能在 UIA 树中定位参数“" + label + "”的输入框。");
        SetRowValue(edit, label, value);
    }

    /// <summary>读取算法参数输入框当前值。</summary>
    /// <param name="label">标签文字。</param>
    /// <returns>当前文本；控件不存在时为空。</returns>
    public string ReadAlgorithmParameter(string label)
    {
        AutomationElement? edit = NamedUi.FindRowEdit(_main, label);
        return edit == null ? string.Empty : NamedUi.ReadEditValue(edit);
    }

    /// <summary>写入“标签同行”的输入框：Value 写入、失焦、回读。</summary>
    /// <param name="edit">输入框。</param>
    /// <param name="label">标签，用于诊断。</param>
    /// <param name="value">目标值。</param>
    private void SetRowValue(AutomationElement edit, string label, string value)
    {
        if (!edit.Patterns.Value.IsSupported)
        {
            throw Verify.Blocked("参数“" + label + "”的输入框未暴露 Value 模式，无法按 UIA 写入。");
        }

        edit.Patterns.Value.Pattern.SetValue(value);
        // 产品使用默认 LostFocus 绑定：把焦点移到同页另一个可聚焦控件后回读。
        AutomationElement? focusTarget = NamedUi.FindButton(_main, "增加");
        focusTarget?.FrameworkAutomationElement.SetFocus();
        UiWait.Until(
            () => string.Equals(NamedUi.ReadEditValue(edit), value, StringComparison.Ordinal),
            _timeout,
            "参数“" + label + "”回读为 " + value);
        _log($"参数“{label}”已写入并回读：{value}");
    }

    /// <summary>07.10 点击“连续采集”。</summary>
    public void StartContinuousGrab() => _ui.Invoke("grabLive", _main);

    /// <summary>07.11 停止连续采集。</summary>
    public void StopContinuousGrab() => _ui.Invoke("grabLive", _main);

    /// <summary>07.11／07.14 点击“采单张”。</summary>
    public void GrabSingle() => _ui.Invoke("grabOne", _main);

    /// <summary>切到“寸动”页签。</summary>
    public void SelectJogTab()
    {
        AutomationElement? tab = NamedUi.FindTab(_main, "寸动");
        if (tab == null)
        {
            throw Verify.Blocked("未能在 UIA 树中定位“寸动”页签。");
        }

        if (tab.Patterns.SelectionItem.IsSupported)
        {
            tab.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            Click(tab, "寸动页签");
        }

        _log("已切换到寸动页签。");
    }

    /// <summary>07.10／07.11／07.13 写入共用“步距”并失焦回读。</summary>
    /// <param name="millimeters">步距文本，例如 0.100。</param>
    public void SetJogStep(string millimeters)
    {
        AutomationElement edit = NamedUi.FindRowEdit(_main, "步距")
            ?? throw Verify.Blocked("未能在寸动区定位“步距”输入框。");
        SetRowValue(edit, "步距", millimeters);
    }

    /// <summary>读取共用“步距”。</summary>
    /// <returns>当前文本。</returns>
    public string ReadJogStep()
    {
        AutomationElement? edit = NamedUi.FindRowEdit(_main, "步距");
        return edit == null ? string.Empty : NamedUi.ReadEditValue(edit);
    }

    /// <summary>
    /// 07.13 逐次点击 X 轴左箭头共指定次数，每次等运动停稳（轴读数稳定）。
    /// 首次点击后核对 X 是否增加；未增加则立即失败并记录，符合原用例“首步读数若未增加则停止”。
    /// </summary>
    /// <param name="times">点击次数。</param>
    /// <returns>点击前后的轴读数。</returns>
    public AxisSnapshot ClickPositiveX(int times)
    {
        AutomationElement button = NamedUi.FindJogDirectionButton(_main, "X", Direction.Left)
            ?? throw Verify.Blocked("未能在寸动区按轴标签定位 X 轴左箭头按钮。");

        Axes before = ReadAxesStable();
        Click(button, "X轴左箭头(第1次)");
        Axes afterFirst = ReadAxesStable();
        if (afterFirst.X <= before.X)
        {
            throw Verify.Failed(
                $"首次点击 X 左箭头后 X 读数未增加（{before.X:0.0000} → {afterFirst.X:0.0000}），" +
                "按用例要求停止记录；请核对该版本左箭头的方向语义。");
        }

        for (int index = 2; index <= times; index++)
        {
            Click(button, $"X轴左箭头(第{index}次)");
            ReadAxesStable();
        }

        return new AxisSnapshot(before.X, afterFirst.X, ReadAxesStable().X, before.Y, ReadAxesStable().Y);
    }

    /// <summary>读取当前轴位置（X/Y/Z/Theta）。</summary>
    /// <returns>轴位置。</returns>
    public Axes ReadAxes() => ReadAxesStable();

    /// <summary>读取轴位置并等待读数稳定。</summary>
    /// <returns>稳定后的轴位置。</returns>
    private Axes ReadAxesStable()
    {
        Axes? last = null;
        int stableCount = 0;
        DateTimeOffset deadline = DateTimeOffset.Now + _timeout;
        while (DateTimeOffset.Now < deadline)
        {
            Axes? current = NamedUi.TryReadAxes(_main);
            if (!current.HasValue)
            {
                throw Verify.Blocked(
                    "未能从页面读到 X/Y/Z/Theta 实际轴位；07.12—07.14 的点位核对缺少可观察轴位来源。");
            }

            Axes value = current.Value;
            if (last.HasValue)
            {
                Axes previous = last.Value;
                bool unchanged = Math.Abs(previous.X - value.X) < 1e-6
                                 && Math.Abs(previous.Y - value.Y) < 1e-6
                                 && Math.Abs(previous.Z - value.Z) < 1e-6;
                if (unchanged)
                {
                    stableCount++;
                    if (stableCount >= 2)
                    {
                        return value;
                    }
                }
                else
                {
                    stableCount = 0;
                }
            }

            last = value;
            Task.Delay(120).GetAwaiter().GetResult();
        }

        throw Verify.Blocked("轴位读数在 " + _timeout.TotalSeconds + " 秒内未稳定。");
    }

    /// <summary>点位列表行数。</summary>
    /// <returns>行数；列表不可读时阻塞。</returns>
    public int PointRowCount() => ReadPointRows().Count;

    /// <summary>读取全部点位行的 X/Y 显示值。</summary>
    /// <returns>行文本。</returns>
    public IReadOnlyList<(string X, string Y)> ReadPointRows()
    {
        var result = new List<(string X, string Y)>();
        AutomationElement? grid = NamedUi.FindPointGrid(_main);
        if (grid == null)
        {
            throw Verify.Blocked("未能在 UIA 树中定位角度点位列表（DataGrid/Table/List）。");
        }


        AutomationElement[] rows = grid.FindAllChildren()
            .Where(element => element.ControlType is
                FlaUI.Core.Definitions.ControlType.DataItem or
                FlaUI.Core.Definitions.ControlType.ListItem)
            .ToArray();
        if (rows.Length == 0)
        {
            // 兜底：行不是标准 DataItem 时，只接受“子级文本中含两个数值”的容器行，避免把表头当数据行。
            rows = grid.FindAllChildren()
                .Where(element => element.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
                    .Count(text => double.TryParse(text.Name, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _)) >= 2)
                .ToArray();
        }

        foreach (AutomationElement row in rows)
        {
            string[] texts = row.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
                .Select(element => element.Name).ToArray();
            result.Add(PickXy(texts));
        }

        return result;
    }

    /// <summary>从一行文本中取前两个数值作为 X/Y 显示值。</summary>
    /// <param name="texts">行内文本。</param>
    /// <returns>X/Y 文本。</returns>
    private static (string X, string Y) PickXy(string[] texts)
    {
        var numbers = texts
            .Where(text => double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            .ToArray();
        return numbers.Length >= 2 ? (numbers[0], numbers[1]) : (string.Empty, string.Empty);
    }

    /// <summary>07.12／07.14 点击“增加”一次。</summary>
    public void AddPoint()
    {
        AutomationElement button = NamedUi.FindButton(_main, "增加")
            ?? throw Verify.Blocked("未能在 UIA 树中定位“增加”按钮。");
        Click(button, "增加点位");
    }

    /// <summary>07.16 点击指定序号点位行的“移动”。</summary>
    /// <param name="rowIndex">从 0 开始的行号。</param>
    public void MoveToPoint(int rowIndex)
    {
        AutomationElement? grid = NamedUi.FindPointGrid(_main);
        if (grid == null)
        {
            throw Verify.Blocked("未能在 UIA 树中定位角度点位列表，无法执行点位“移动”。");
        }

        AutomationElement[] rows = grid.FindAllChildren()
            .Where(element => element.ControlType is
                FlaUI.Core.Definitions.ControlType.DataItem or
                FlaUI.Core.Definitions.ControlType.ListItem)
            .ToArray();
        if (rowIndex >= rows.Length)
        {
            throw Verify.Failed($"点位列表只有 {rows.Length} 行，取不到第 {rowIndex + 1} 行。");
        }

        AutomationElement? move = NamedUi.FindButton(rows[rowIndex], "移动")
            ?? throw Verify.Blocked("未能在第 " + (rowIndex + 1) + " 个点位行定位“移动”按钮。");
        Click(move, "点位移动");
    }

    /// <summary>07.16 点击“采图调试”。</summary>
    public void DebugCapture()
    {
        AutomationElement button = NamedUi.FindButton(_main, "采图调试")
            ?? throw Verify.Blocked("未能在 UIA 树中定位“采图调试”按钮。");
        Click(button, "采图调试");
    }

    /// <summary>07.15 点击页面底部“保存”。</summary>
    public void Save()
    {
        _ui.Invoke("RecipeStep.Save", _main);
        _log("已点击配方保存。");
    }

    /// <summary>07.17 点击页面底部“运行”，返回弹窗正文。</summary>
    /// <param name="timeout">等待弹窗时间。</param>
    /// <returns>弹窗正文。</returns>
    public string RunAndReadPrompt(TimeSpan timeout)
    {
        _ui.Invoke("RecipeStep.Run", _main);
        AutomationElement prompt = _ui.Find("MessageBox.Content", timeout);
        string text = prompt.Name ?? string.Empty;
        _log("运行结果弹窗：" + text);
        return text;
    }

    /// <summary>确认当前消息框。</summary>
    public void ConfirmPrompt() => _ui.Invoke("MessageBox.OK", _main);

    /// <summary>是否存在消息框。</summary>
    /// <returns>存在为 true。</returns>
    public bool HasMessageBox() => _ui.Optional("MessageBox.Content", _main) != null;

    /// <summary>读取“角度偏移”结果框。</summary>
    /// <returns>文本。</returns>
    public string ReadAngleOffset() => ReadResultValue("角度偏移");

    /// <summary>读取“纠正后角度”结果框。</summary>
    /// <returns>文本。</returns>
    public string ReadCorrectedAngle() => ReadResultValue("纠正后角度");

    /// <summary>读取结果框：按标签同行定位，只读不写。</summary>
    /// <param name="label">标签文字。</param>
    /// <returns>文本；缺失时为空。</returns>
    private string ReadResultValue(string label)
    {
        AutomationElement? edit = NamedUi.FindRowEdit(_main, label);
        return edit == null ? string.Empty : NamedUi.ReadEditValue(edit);
    }

    /// <summary>
    /// 采集图像区统计信息：像素最小/最大值、平均亮度与对比度，用于“非全黑/全白、画面有内容”的客观判断。
    /// 只读取屏幕截图，不解析 Cognex 内部图像对象，也不等同于算法识别成功。
    /// </summary>
    /// <param name="result">统计结果。</param>
    /// <returns>采集成功时为 true。</returns>
    public bool TryMeasureImageView(out ImageStatistics result)
    {
        result = default;
        AutomationElement? host = NamedUi.FindImageHost(_main, ImageHostIds)
            ?? NamedUi.FindLargestPane(_main);
        if (host == null)
        {
            return false;
        }

        using Bitmap? bitmap = FlaUI.Core.Capturing.Capture.Element(host).Bitmap;
        if (bitmap == null || bitmap.Width < 8 || bitmap.Height < 8)
        {
            return false;
        }

        long sum = 0;
        int count = 0;
        int min = 255;
        int max = 0;
        int stepX = Math.Max(1, bitmap.Width / 64);
        int stepY = Math.Max(1, bitmap.Height / 64);
        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                Color pixel = bitmap.GetPixel(x, y);
                int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                sum += luminance;
                count++;
                if (luminance < min) min = luminance;
                if (luminance > max) max = luminance;
            }
        }

        result = new ImageStatistics(count == 0 ? 0 : sum / (double)count, min, max, count);
        return count > 0;
    }

    /// <summary>点击一个 UIA 元素；优先 InvokePattern，其次 SelectionItem，最后 UIA 默认动作。</summary>
    /// <param name="element">目标控件。</param>
    /// <param name="description">业务描述。</param>
    private void Click(AutomationElement element, string description)
    {
        UiWait.Until(() => element.IsEnabled && !element.IsOffscreen, _timeout, description + "可操作");
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return;
        }

        if (element.Properties.NativeWindowHandle.ValueOrDefault != IntPtr.Zero)
        {
            element.Focus();
            element.Click();
            return;
        }

        throw Verify.Blocked($"控件“{description}”未暴露可用的 UIA 动作（Invoke/SelectionItem/Toggle/NativeWindow）。");
    }

    /// <summary>按文字选择组合框选项并回读。</summary>
    /// <param name="combo">组合框。</param>
    /// <param name="text">精确选项文字。</param>
    /// <param name="description">业务描述。</param>
    private void SelectComboBoxItemByText(ComboBox combo, string text, string description)
    {
        SoftwareUi.RequirePattern(combo.Patterns.ExpandCollapse.IsSupported, SafeId(combo), "ExpandCollapse");
        combo.Expand();
        try
        {
            ComboBoxItem item = UiWait.UntilNotNull(
                () => combo.Items.FirstOrDefault(candidate =>
                    candidate.IsEnabled &&
                    !candidate.IsOffscreen &&
                    (string.Equals(candidate.Name, text, StringComparison.Ordinal) ||
                     candidate.FindAllDescendants(cf => cf.ByName(text)).Length == 1)),
                _timeout,
                $"{description}下拉框中出现选项 '{text}'");
            item.Select();
            UiWait.Until(
                () => combo.SelectedItem != null &&
                      (string.Equals(combo.SelectedItem.Name, text, StringComparison.Ordinal) ||
                       combo.SelectedItem.FindAllDescendants(cf => cf.ByName(text)).Length == 1),
                _timeout,
                $"{description}回读为 '{text}'");
        }
        finally
        {
            try
            {
                combo.Collapse();
            }
            catch (Exception)
            {
                // 已自动收起时忽略。
            }
        }
    }

    /// <summary>判断组合框是否包含指定选项文字（不展开时读取已生成选项）。</summary>
    /// <param name="element">组合框元素。</param>
    /// <param name="text">选项文字。</param>
    /// <returns>包含时为 true。</returns>
    private static bool ComboContains(AutomationElement element, string text)
    {
        try
        {
            ComboBox combo = element.AsComboBox();
            return combo.Items.Any(item =>
                string.Equals(item.Name, text, StringComparison.Ordinal) ||
                item.FindAllDescendants(cf => cf.ByName(text)).Length == 1);
        }
        catch (Exception)
        {
            // 未生成选项容器时展开一次再判断，避免把“暂时读不到”当成“不包含”。
            try
            {
                ComboBox combo = element.AsComboBox();
                combo.Expand();
                bool found = combo.Items.Any(item =>
                    string.Equals(item.Name, text, StringComparison.Ordinal) ||
                    item.FindAllDescendants(cf => cf.ByName(text)).Length == 1);
                combo.Collapse();
                return found;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>等待勾选框达到期望状态。</summary>
    /// <param name="checkBox">勾选框。</param>
    /// <param name="expected">期望状态。</param>
    /// <param name="description">业务描述。</param>
    private void WaitChecked(CheckBox checkBox, bool expected, string description) =>
        UiWait.Until(() => checkBox.IsChecked == expected, _timeout, description);

    /// <summary>安全读取 AutomationId。</summary>
    /// <param name="element">元素。</param>
    /// <returns>ID 或空串。</returns>
    private static string SafeId(AutomationElement element)
    {
        try
        {
            return element.AutomationId ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>寸动方向。</summary>
    internal enum Direction
    {
        /// <summary>行内最左按钮。</summary>
        Left,

        /// <summary>行内最右按钮。</summary>
        Right
    }
}

/// <summary>轴位置读数。</summary>
internal readonly struct Axes
{
    /// <summary>创建轴位置。</summary>
    /// <param name="x">X。</param>
    /// <param name="y">Y。</param>
    /// <param name="z">Z。</param>
    /// <param name="theta">Theta。</param>
    public Axes(double x, double y, double z, double theta)
    {
        X = x;
        Y = y;
        Z = z;
        Theta = theta;
    }

    /// <summary>X 轴位置（mm）。</summary>
    public double X { get; }

    /// <summary>Y 轴位置（mm）。</summary>
    public double Y { get; }

    /// <summary>Z 轴位置（mm）。</summary>
    public double Z { get; }

    /// <summary>Theta 角（度）。</summary>
    public double Theta { get; }
}

/// <summary>07.13 位移核验读数。</summary>
internal readonly struct AxisSnapshot
{
    /// <summary>创建位移读数。</summary>
    /// <param name="xBefore">点击前 X。</param>
    /// <param name="xAfterFirst">首次点击后 X。</param>
    /// <param name="xAfterAll">全部点击后 X。</param>
    /// <param name="yBefore">点击前 Y。</param>
    /// <param name="yAfter">全部点击后 Y。</param>
    public AxisSnapshot(double xBefore, double xAfterFirst, double xAfterAll, double yBefore, double yAfter)
    {
        XBefore = xBefore;
        XAfterFirst = xAfterFirst;
        XAfterAll = xAfterAll;
        YBefore = yBefore;
        YAfter = yAfter;
    }

    /// <summary>点击前 X。</summary>
    public double XBefore { get; }

    /// <summary>首次点击后 X。</summary>
    public double XAfterFirst { get; }

    /// <summary>全部点击后 X。</summary>
    public double XAfterAll { get; }

    /// <summary>点击前 Y。</summary>
    public double YBefore { get; }

    /// <summary>全部点击后 Y。</summary>
    public double YAfter { get; }
}

/// <summary>图像区亮度统计。</summary>
internal readonly struct ImageStatistics
{
    /// <summary>创建统计结果。</summary>
    /// <param name="mean">平均亮度。</param>
    /// <param name="min">最小亮度。</param>
    /// <param name="max">最大亮度。</param>
    /// <param name="samples">采样点数。</param>
    public ImageStatistics(double mean, int min, int max, int samples)
    {
        Mean = mean;
        Min = min;
        Max = max;
        Samples = samples;
    }

    /// <summary>平均亮度（0—255）。</summary>
    public double Mean { get; }

    /// <summary>最小亮度。</summary>
    public int Min { get; }

    /// <summary>最大亮度。</summary>
    public int Max { get; }

    /// <summary>采样点数。</summary>
    public int Samples { get; }

    /// <summary>对比度（最大—最小），用于非全黑/全白判断。</summary>
    public int Contrast => Max - Min;
}
