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

    /// <summary>
    /// 按行标签定位同一行的勾选框。
    /// 本页勾选框本身没有可访问名称（Name 为空），“明场照明”等文字是独立的 Text，
    /// 所以不能按“含明场的勾选框”找；标签会以“容器 Text + 内部 Text”重复出现，按矩形去重后
    /// 要求恰好解析出一个勾选框。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <param name="label">行标签文字。</param>
    /// <returns>唯一勾选框；缺失或多义时返回 null。</returns>
    public static CheckBox? FindRowCheckBox(AutomationElement root, string label)
    {
        AutomationElement[] labels = DedupeByRect(Descendants(root).Where(element =>
            SafeControlType(element) == ControlType.Text && NameContains(element, label)));
        CheckBox[] checkBoxes = Descendants(root)
            .Where(element => SafeControlType(element) == ControlType.CheckBox)
            .Select(element => element.AsCheckBox())
            .ToArray();

        var resolved = new List<CheckBox>();
        foreach (AutomationElement anchor in labels)
        {
            Rectangle anchorRect = Rect(anchor);
            if (anchorRect.IsEmpty)
            {
                continue;
            }

            CheckBox? best = null;
            double bestDistance = double.MaxValue;
            foreach (CheckBox checkBox in checkBoxes)
            {
                Rectangle rect = Rect(checkBox);
                if (rect.IsEmpty || Math.Abs(CenterY(rect) - CenterY(anchorRect)) > RowTolerancePx)
                {
                    continue;
                }

                double distance = Math.Abs(rect.X - anchorRect.Right);
                if (distance < bestDistance)
                {
                    best = checkBox;
                    bestDistance = distance;
                }
            }

            if (best != null && resolved.All(existing => RuntimeKey(existing) != RuntimeKey(best)))
            {
                resolved.Add(best);
            }
        }

        return resolved.Count == 1 ? resolved[0] : null;
    }

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
    ///
    /// 页面上的红色说明文字也含参数名（例如“低阈值:(范围0-255)要求大于切割道灰度值…”），
    /// 所以逐字相等的标签优先；只有没有逐字标签时才退回包含匹配，并按“同行输入框”的唯一性裁决。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <param name="label">标签文字。</param>
    /// <returns>输入框；歧义或缺失时为 null。</returns>
    public static AutomationElement? FindRowEdit(AutomationElement root, string label)
    {
        AutomationElement[] texts = Descendants(root)
            .Where(element => SafeControlType(element) == ControlType.Text && NameContains(element, label))
            .ToArray();
        if (texts.Length == 0)
        {
            return null;
        }

        AutomationElement[] exact = texts.Where(element => NameEquals(element, label)).ToArray();
        AutomationElement[] anchors = exact.Length > 0 ? exact : texts;

        AutomationElement[] edits = Descendants(root)
            .Where(element => SafeControlType(element) is ControlType.Edit or ControlType.Spinner)
            .ToArray();

        var resolved = new List<AutomationElement>();
        foreach (AutomationElement anchor in anchors)
        {
            AutomationElement? edit = RowEdit(anchor, edits);
            if (edit != null && !resolved.Any(existing => existing.Equals(edit)))
            {
                resolved.Add(edit);
            }
        }

        return resolved.Count == 1 ? resolved[0] : null;
    }

    /// <summary>
    /// 在锚点同一行内取最近的输入框；同行没有时只在“恰好含一个输入框”的容器内取，
    /// 避免在参数区这种多行 Grid 里退回到别的行的输入框。
    /// </summary>
    /// <param name="anchor">标签锚点。</param>
    /// <param name="edits">页面内全部输入框快照。</param>
    /// <returns>输入框；无法唯一确定时为 null。</returns>
    private static AutomationElement? RowEdit(AutomationElement anchor, AutomationElement[] edits)
    {
        Rectangle anchorRect = Rect(anchor);
        if (!anchorRect.IsEmpty)
        {
            AutomationElement? nearest = null;
            double nearestDistance = double.MaxValue;
            foreach (AutomationElement edit in edits)
            {
                Rectangle rect = Rect(edit);
                if (rect.IsEmpty || Math.Abs(CenterY(rect) - CenterY(anchorRect)) > RowTolerancePx)
                {
                    continue;
                }

                double distance = Math.Abs(rect.X - anchorRect.Right);
                if (distance < nearestDistance)
                {
                    nearest = edit;
                    nearestDistance = distance;
                }
            }

            if (nearest != null)
            {
                return nearest;
            }
        }

        AutomationElement? container = anchor.Parent;
        for (int depth = 0; depth < 3 && container != null; depth++, container = container.Parent)
        {
            AutomationElement[] inside = container
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                .Take(2)
                .ToArray();
            if (inside.Length == 1)
            {
                return inside[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 说明 FindRowEdit 没能唯一定位的原因：含该文字的元素总数、其中逐字标签数、同行命中的输入框数。
    /// 只用于失败信息，不参与定位判断。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <param name="label">标签文字。</param>
    /// <returns>简短诊断文字。</returns>
    public static string ExplainRowEdit(AutomationElement root, string label)
    {
        AutomationElement[] texts = Descendants(root)
            .Where(element => SafeControlType(element) == ControlType.Text && NameContains(element, label))
            .ToArray();
        int exactCount = texts.Count(element => NameEquals(element, label));
        AutomationElement[] edits = Descendants(root)
            .Where(element => SafeControlType(element) is ControlType.Edit or ControlType.Spinner)
            .ToArray();

        var rowEdits = new HashSet<string>(StringComparer.Ordinal);
        foreach (AutomationElement anchor in texts)
        {
            AutomationElement? edit = RowEdit(anchor, edits);
            if (edit != null)
            {
                rowEdits.Add(RuntimeKey(edit));
            }
        }

        return "含该文字的元素 " + texts.Length + " 个（逐字标签 " + exactCount + " 个），同行命中输入框 " +
               rowEdits.Count + " 个";
    }

    /// <summary>元素运行时标识，用于对 UIA 元素去重。</summary>
    /// <param name="element">元素。</param>
    /// <returns>运行时可唯一定位的键。</returns>
    private static string RuntimeKey(AutomationElement element)
    {
        try
        {
            return string.Join(",", element.Properties.RuntimeId.Value);
        }
        catch (Exception)
        {
            return Guid.NewGuid().ToString("N");
        }
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

    /// <summary>轴位标签文字：X: / Y： / Z= / T: / Theta:（不带数值）。</summary>
    private static readonly Regex AxisLabelPattern = new(
        @"^(?<axis>X|Y|Z|T|THETA|Θ|θ)\s*[:：=]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 从页面文字中读取 X/Y/Z/Theta 实际轴位。
    /// 兼容两种页面写法：
    /// 1) 一个元素内就是“X: 12.3456”；
    /// 2) 标签 Text（“X:”）与数值 Edit 是两个相邻元素、同一行（本页角度校正页即此写法）。
    /// 两种都读不到 X/Y 时返回 null，不推断。
    /// </summary>
    /// <param name="root">查找根。</param>
    /// <returns>轴位；缺失时为 null。</returns>
    public static Axes? TryReadAxes(AutomationElement root)
    {
        AutomationElement[] elements = Descendants(root);
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (AutomationElement element in elements)
        {
            if (SafeControlType(element) is not (ControlType.Text or ControlType.Edit))
            {
                continue;
            }

            Match match = AxisPattern.Match(SafeName(element).Trim());
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double inline))
            {
                values[NormalizeAxis(match.Groups["axis"].Value)] = inline;
            }
        }

        AutomationElement[] edits = elements.Where(element => SafeControlType(element) == ControlType.Edit).ToArray();
        foreach (AutomationElement label in DedupeByRect(elements.Where(element =>
                     SafeControlType(element) == ControlType.Text)))
        {
            Match match = AxisLabelPattern.Match(SafeName(label).Trim());
            if (!match.Success)
            {
                continue;
            }

            string axis = NormalizeAxis(match.Groups["axis"].Value);
            if (values.ContainsKey(axis))
            {
                continue;
            }

            AutomationElement? edit = RowEdit(label, edits);
            if (edit == null)
            {
                continue;
            }

            if (double.TryParse(ReadEditValue(edit), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double paired))
            {
                values[axis] = paired;
            }
        }

        if (!values.TryGetValue("X", out double x) || !values.TryGetValue("Y", out double y))
        {
            return null;
        }

        values.TryGetValue("Z", out double z);
        values.TryGetValue("THETA", out double theta);
        return new Axes(x, y, z, theta);
    }

    /// <summary>轴名归一化：T/Θ 都按 Theta。</summary>
    /// <param name="axis">读到的轴名。</param>
    /// <returns>归一化轴名。</returns>
    private static string NormalizeAxis(string axis)
    {
        string upper = axis.ToUpperInvariant();
        return upper is "T" or "Θ" ? "THETA" : upper;
    }

    /// <summary>安全读取元素名称。</summary>
    /// <param name="element">元素。</param>
    /// <returns>名称或空串。</returns>
    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>按矩形去重：WPF 常把同一标签暴露成“容器 Text + 内部 Text”，两张同名同位置。</summary>
    /// <param name="elements">候选元素。</param>
    /// <returns>去重后的元素。</returns>
    private static AutomationElement[] DedupeByRect(IEnumerable<AutomationElement> elements)
    {
        var result = new List<AutomationElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AutomationElement element in elements)
        {
            Rectangle rect = Rect(element);
            string key = rect.IsEmpty ? "empty:" + RuntimeKey(element) : rect.ToString();
            if (seen.Add(key))
            {
                result.Add(element);
            }
        }

        return result.ToArray();
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
        AutomationElement? label = Unique(DedupeByRect(Descendants(root).Where(element =>
            SafeControlType(element) == ControlType.Text && NameEquals(element, axis))));
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

    /// <summary>配方步骤导航树 AutomationId（生产源码 TreeView x:Name）。</summary>
    public const string NavigationTreeId = "ucTree";

    /// <summary>
    /// 在配方步骤导航树内按标签文字定位唯一的 TreeViewItem。
    /// 生产程序尚未在 TreeViewItem 上暴露 AutomationId 时使用：同一标签会同时出现在
    /// 行内 Text 与其内部 Text 上，先归并到最近的 TreeItem 祖先，再要求恰好一个；
    /// 缺失或多义时返回 null，由调用方阻塞，不按序号或坐标猜测。
    /// </summary>
    /// <param name="root">查找根（主窗口）。</param>
    /// <param name="label">步骤显示文字。</param>
    /// <returns>唯一树项；缺失或多义时返回 null。</returns>
    public static TreeItem? FindNavigationTreeItem(AutomationElement root, string label)
    {
        AutomationElement? tree = root.FindFirstDescendant(cf => cf.ByAutomationId(NavigationTreeId));
        if (tree == null)
        {
            return null;
        }

        var items = new Dictionary<string, TreeItem>(StringComparer.Ordinal);
        foreach (AutomationElement named in Descendants(tree).Where(element => NameEquals(element, label)))
        {
            AutomationElement? current = named;
            while (current != null && !current.Equals(tree) && SafeControlType(current) != ControlType.TreeItem)
            {
                current = current.Parent;
            }

            if (current == null || current.Equals(tree))
            {
                continue;
            }

            string runtimeId;
            try
            {
                runtimeId = string.Join(",", current.Properties.RuntimeId.Value);
            }
            catch (Exception)
            {
                continue;
            }

            items[runtimeId] = current.AsTreeItem();
        }

        return items.Count == 1 ? items.Values.First() : null;
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
