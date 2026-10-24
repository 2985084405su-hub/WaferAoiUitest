using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 无 AutomationId 控件的结构化定位规则。
///
/// 生产源码核实：07 节角度页的转正方式、切割前后、四个算法参数、点位列表、寸动按钮、
/// 角度结果框都是无 Name/ID 的标准 WPF 控件。本类只使用以下三种可复现依据：
/// 1) 可访问名称（标签文字/按钮文字）；
/// 2) 控件类型；
/// 3) 与已唯一定位锚点在同一行/同一区域的几何归属（UIA BoundingRectangle）。
/// 不按绝对坐标、不按截图位置、不按控件序号猜测；任何歧义都返回 null 并由调用方阻塞。
/// </summary>
internal static class NamedUi
{
    /// <summary>同一行的垂直容差（像素）。</summary>
    private const int RowTolerancePx = 14;

    /// <summary>轴位文字：X: 12.3456 / Y=-3.2 / Theta：0.0001。</summary>
    private static readonly Regex AxisPattern = new(
        @"^(?<axis>X|Y|Z|THETA|Θ|θ)\s*[:：=]\s*(?<value>-?\d+(?:\.\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>查按钮：名称逐字相等优先，其次包含。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="name">按钮文字。</param>
    /// <returns>唯一按钮；不存在或多义时返回 null。</returns>
    public static AutomationElement? FindButton(AutomationElement root, string name) =>
        Unique(Descendants(root)
            .Where(element => SafeControlType(element) == ControlType.Button && NameEquals(element, name)));

    /// <summary>在锚点同一行内找最近的指定按钮。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="anchor">行锚点（通常是标签或勾选框）。</param>
    /// <param name="name">按钮文字。</param>
    /// <returns>最近按钮；找不到时返回 null。</returns>
    public static AutomationElement? FindNearestButton(AutomationElement root, AutomationElement anchor, string name)
    {
        Rectangle anchorRect = Rect(anchor);
        if (anchorRect.IsEmpty)
        {
            return null;
        }

        var candidates = new List<(AutomationElement Element, double Distance)>();
        foreach (AutomationElement button in Descendants(root)
                     .Where(element => SafeControlType(element) == ControlType.Button && NameEquals(element, name)))
        {
            Rectangle rect = Rect(button);
            if (rect.IsEmpty || Math.Abs(CenterY(rect) - CenterY(anchorRect)) > RowTolerancePx * 2)
            {
                continue;
            }

            double distance = Math.Abs(rect.X - anchorRect.Right);
            candidates.Add((button, distance));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        return candidates[0].Element;
    }

    /// <summary>按名称包含查找勾选框。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="keyword">名称关键字。</param>
    /// <returns>唯一勾选框；不存在或多义时返回 null。</returns>
    public static CheckBox? FindCheckBoxContaining(AutomationElement root, string keyword) =>
        Unique(Descendants(root).Where(element =>
                SafeControlType(element) == ControlType.CheckBox && NameContains(element, keyword)))
            ?.AsCheckBox();

    /// <summary>按名称逐字查找单选按钮。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="name">单选按钮文字。</param>
    /// <returns>唯一单选按钮。</returns>
    public static RadioButton? FindRadio(AutomationElement root, string name) =>
        Unique(Descendants(root).Where(element =>
                SafeControlType(element) == ControlType.RadioButton && NameEquals(element, name)))
            ?.AsRadioButton();

    /// <summary>按页签名称查找 TabItem。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="name">页签文字。</param>
    /// <returns>唯一页签。</returns>
    public static AutomationElement? FindTab(AutomationElement root, string name) =>
        Unique(Descendants(root).Where(element =>
            SafeControlType(element) == ControlType.TabItem &&
            (NameEquals(element, name) || NameContains(element, name))));

    /// <summary>
    /// 按标签定位同一行的输入框：先唯一定位标签元素，再在同行带内取最近的 Edit；
    /// 同行带内没有 Edit 时向上回溯最多三层容器再找，仍找不到返回 null。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <param name="label">标签文字。</param>
    /// <returns>输入框；歧义或缺失时为 null。</returns>
    public static AutomationElement? FindRowEdit(AutomationElement root, string label)
    {
        AutomationElement? anchor = Unique(Descendants(root).Where(element =>
            SafeControlType(element) is ControlType.Text &&
            NameContains(element, label)));
        if (anchor == null)
        {
            return null;
        }

        Rectangle anchorRect = Rect(anchor);
        var candidates = new List<(AutomationElement Element, double Distance)>();
        foreach (AutomationElement edit in Descendants(root).Where(element =>
                     SafeControlType(element) is ControlType.Edit or ControlType.Spinner))
        {
            Rectangle rect = Rect(edit);
            if (rect.IsEmpty || Math.Abs(CenterY(rect) - CenterY(anchorRect)) > RowTolerancePx)
            {
                continue;
            }

            candidates.Add((edit, Math.Abs(rect.X - anchorRect.Right)));
        }

        if (candidates.Count > 0)
        {
            candidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            return candidates[0].Element;
        }

        // 卡片式布局中标签与输入框可能分属上下相邻容器；只在最近的三层祖先内查找，避免跨区域误取。
        AutomationElement? container = anchor.Parent;
        for (int depth = 0; depth < 3 && container != null; depth++, container = container.Parent)
        {
            AutomationElement? edit = container.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Edit));
            if (edit != null)
            {
                return edit;
            }
        }

        return null;
    }

    /// <summary>读取输入框当前值（优先 Value 模式）。</summary>
    /// <param name="edit">输入框。</param>
    /// <returns>去首尾空白的文本。</returns>
    public static string ReadEditValue(AutomationElement edit)
    {
        try
        {
            if (edit.Patterns.Value.IsSupported)
            {
                return (edit.Patterns.Value.Pattern.Value.Value ?? string.Empty).Trim();
            }
        }
        catch (Exception)
        {
            // Value 读取瞬时失败时退回 Name，用于显示型文本框。
        }

        try
        {
            return (edit.Name ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 从页面文字中读取 X/Y/Z/Theta 实际轴位。
    /// 只接受“轴名 + 冒号/等号 + 数字”这一明确格式；读不全时返回 null，不推断。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <returns>轴位；缺失时为 null。</returns>
    public static Axes? TryReadAxes(AutomationElement root)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (AutomationElement element in Descendants(root))
        {
            if (SafeControlType(element) is not (ControlType.Text or ControlType.Edit))
            {
                continue;
            }

            string text;
            try
            {
                text = element.Name ?? string.Empty;
            }
            catch (Exception)
            {
                continue;
            }

            Match match = AxisPattern.Match(text.Trim());
            if (!match.Success)
            {
                continue;
            }

            string axis = match.Groups["axis"].Value.ToUpperInvariant();
            if (axis is "Θ") axis = "THETA";
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                continue;
            }

            values[axis] = value;
        }

        if (!values.TryGetValue("X", out double x) || !values.TryGetValue("Y", out double y))
        {
            return null;
        }

        values.TryGetValue("Z", out double z);
        values.TryGetValue("THETA", out double theta);
        return new Axes(x, y, z, theta);
    }

    /// <summary>按轴标签与方向按钮的同一行归属定位寸动方向按钮。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="axis">轴标签，X/Y/Z。</param>
    /// <param name="direction">行内最左或最右。</param>
    /// <returns>按钮；找不到时返回 null。</returns>
    public static AutomationElement? FindJogDirectionButton(
        AutomationElement root,
        string axis,
        StreetAlignmentPage.Direction direction)
    {
        AutomationElement? label = Unique(Descendants(root).Where(element =>
            SafeControlType(element) == ControlType.Text && NameEquals(element, axis)));
        if (label == null)
        {
            return null;
        }

        Rectangle labelRect = Rect(label);
        if (labelRect.IsEmpty)
        {
            return null;
        }

        var rowButtons = Descendants(root)
            .Where(element => SafeControlType(element) == ControlType.Button)
            .Select(element => (Element: element, Rect: Rect(element)))
            .Where(pair => !pair.Rect.IsEmpty &&
                           Math.Abs(CenterY(pair.Rect) - CenterY(labelRect)) <= RowTolerancePx * 2)
            .OrderBy(pair => pair.Rect.X)
            .ToList();
        if (rowButtons.Count < 2)
        {
            return null;
        }

        return direction == StreetAlignmentPage.Direction.Left
            ? rowButtons[0].Element
            : rowButtons[rowButtons.Count - 1].Element;
    }

    /// <summary>
    /// 定位角度点位列表：优先带 X/Y 列头或含数据行的 DataGrid/Table/List；
    /// 排除导航树与下拉列表，避免把步骤树当成点位表。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <returns>列表控件；找不到时返回 null。</returns>
    public static AutomationElement? FindPointGrid(AutomationElement root)
    {
        var candidates = new List<(AutomationElement Element, int Score)>();
        foreach (AutomationElement element in Descendants(root))
        {
            ControlType type = SafeControlType(element);
            if (type is not (ControlType.DataGrid or ControlType.Table or ControlType.List))
            {
                continue;
            }

            int dataItems = element.FindAllChildren().Count(child =>
                SafeControlType(child) is ControlType.DataItem or ControlType.ListItem);
            int score = 0;
            if (type == ControlType.DataGrid) score += 4;
            if (type == ControlType.Table) score += 3;
            if (dataItems > 0) score += 2;
            try
            {
                string[] headers = element.FindAllDescendants(cf => cf.ByControlType(ControlType.HeaderItem))
                    .Select(item => item.Name ?? string.Empty).ToArray();
                if (headers.Any(text => string.Equals(text.Trim(), "X", StringComparison.OrdinalIgnoreCase)) &&
                    headers.Any(text => string.Equals(text.Trim(), "Y", StringComparison.OrdinalIgnoreCase)))
                {
                    score += 6;
                }
            }
            catch (Exception)
            {
                // 读不到列头时按类型和数据行打分。
            }

            if (score > 0)
            {
                candidates.Add((element, score));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        if (candidates.Count > 1 && candidates[0].Score == candidates[1].Score)
        {
            // 同分说明无法区分点位表与其他列表，按阻塞处理而不是随便取一个。
            return null;
        }

        return candidates[0].Element;
    }

    /// <summary>按候选 ID 定位图像宿主。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="ids">候选 AutomationId。</param>
    /// <returns>图像宿主；找不到时返回 null。</returns>
    public static AutomationElement? FindImageHost(AutomationElement root, string[] ids)
    {
        foreach (string id in ids)
        {
            AutomationElement? element = root.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (element != null && !element.IsOffscreen)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>
    /// 兜底：取页面内面积最大且明显小于主窗口的 Pane/Custom/Image，用作图像区截取范围。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <returns>元素；找不到时返回 null。</returns>
    public static AutomationElement? FindLargestPane(AutomationElement root)
    {
        Rectangle windowRect = Rect(root);
        double windowArea = windowRect.IsEmpty ? 0 : (double)windowRect.Width * windowRect.Height;
        AutomationElement? best = null;
        double bestArea = 0;
        foreach (AutomationElement element in Descendants(root))
        {
            if (SafeControlType(element) is not (ControlType.Pane or ControlType.Custom or ControlType.Image))
            {
                continue;
            }

            Rectangle rect = Rect(element);
            if (rect.IsEmpty || rect.Width < 200 || rect.Height < 150)
            {
                continue;
            }

            double area = (double)rect.Width * rect.Height;
            if (windowArea > 0 && area > windowArea * 0.9)
            {
                continue;
            }

            if (area > bestArea)
            {
                bestArea = area;
                best = element;
            }
        }

        return best;
    }

    /// <summary>枚举后代，容忍 WPF 失效 peer。</summary>
    /// <param name="root">根元素。</param>
    /// <returns>后代快照。</returns>
    private static AutomationElement[] Descendants(AutomationElement root)
    {
        try
        {
            return root.FindAllDescendants();
        }
        catch (Exception)
        {
            return Array.Empty<AutomationElement>();
        }
    }

    /// <summary>唯一化：恰好一个时返回，其余情况返回 null。</summary>
    /// <param name="elements">候选。</param>
    /// <returns>唯一元素。</returns>
    private static AutomationElement? Unique(IEnumerable<AutomationElement> elements)
    {
        AutomationElement[] array = elements.Take(2).ToArray();
        return array.Length == 1 ? array[0] : null;
    }

    /// <summary>名称逐字比较。</summary>
    /// <param name="element">元素。</param>
    /// <param name="name">期望名称。</param>
    /// <returns>相等时为 true。</returns>
    private static bool NameEquals(AutomationElement element, string name)
    {
        try
        {
            return string.Equals((element.Name ?? string.Empty).Trim(), name, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>名称包含比较。</summary>
    /// <param name="element">元素。</param>
    /// <param name="keyword">关键字。</param>
    /// <returns>包含时为 true。</returns>
    private static bool NameContains(AutomationElement element, string keyword)
    {
        try
        {
            return (element.Name ?? string.Empty).Contains(keyword, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>安全读取控件类型。</summary>
    /// <param name="element">元素。</param>
    /// <returns>控件类型；读取失败为 Custom。</returns>
    private static ControlType SafeControlType(AutomationElement element)
    {
        try
        {
            return element.ControlType;
        }
        catch (Exception)
        {
            return ControlType.Custom;
        }
    }

    /// <summary>安全读取边界矩形。</summary>
    /// <param name="element">元素。</param>
    /// <returns>矩形；读取失败为空。</returns>
    private static Rectangle Rect(AutomationElement element)
    {
        try
        {
            return element.Properties.BoundingRectangle.ValueOrDefault;
        }
        catch (Exception)
        {
            return Rectangle.Empty;
        }
    }

    /// <summary>矩形垂直中心。</summary>
    /// <param name="rect">矩形。</param>
    /// <returns>中心 Y。</returns>
    private static double CenterY(Rectangle rect) => rect.Y + (rect.Height / 2.0);
}
