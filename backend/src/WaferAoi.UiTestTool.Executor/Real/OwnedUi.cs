using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 绑定本轮被测进程的 UIA 操作：只按 AutomationId 定位，要求在查找根内唯一，再核对进程归属。
/// PID 只用于核对归属，不用于筛除 ID 歧义；不提供文字、索引、坐标或键鼠定位回退。
/// 迁移自源工程 Drivers/OwnedUi.cs，断言出口改为 Verify（失败/阻塞语义不变）。
/// </summary>
internal sealed class OwnedUi
{
    /// <summary>登录账号组合框 ID；按配置账号精确选择属于全局获准的定位例外。</summary>
    public const string LoginAccountId = "HardwareStatus.UserName";

    /// <summary>本轮拥有的进程与自动化会话。</summary>
    private readonly WaferAoiApplication _application;

    /// <summary>超时配置。</summary>
    private readonly ExecutorSettings _settings;

    /// <summary>只记录 AutomationId 与耗时，不记录输入值及密码。</summary>
    private readonly Action<string> _log;

    /// <summary>获准的“组合框 ID → 精确选项文字”例外。</summary>
    private readonly IReadOnlyDictionary<string, string> _approvedSelections;

    /// <summary>创建绑定本轮进程的 UI 操作器。</summary>
    /// <param name="application">本轮程序驱动。</param>
    /// <param name="settings">账号与超时配置。</param>
    /// <param name="approvedSelections">调用方获准的组合框精确选项。</param>
    /// <param name="log">操作耗时日志。</param>
    public OwnedUi(
        WaferAoiApplication application,
        ExecutorSettings settings,
        IReadOnlyDictionary<string, string>? approvedSelections,
        Action<string> log)
    {
        _application = application;
        _settings = settings;
        _log = log;
        var approved = (approvedSelections ?? new Dictionary<string, string>())
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        approved[LoginAccountId] = settings.UserName;
        _approvedSelections = approved;
    }

    /// <summary>进程退出即终止等待，不持续重试已不存在的窗口。</summary>
    public void EnsureRunning()
    {
        if (!_application.IsRunning)
        {
            throw new UiWaitTerminatedException("本轮启动的被测程序已退出，停止等待；详见失败证据。");
        }
    }

    /// <summary>只按 ID 查找；重复或其他进程归属属于阻塞。</summary>
    /// <param name="id">控件 AutomationId。</param>
    /// <param name="root">查找根；为空时按已核实的窗口 ID 限定范围。</param>
    /// <returns>唯一且属于本进程的控件；不存在时返回空。</returns>
    public AutomationElement? Optional(string id, AutomationElement? root = null)
    {
        EnsureRunning();
        if (root == null)
        {
            string? windowId = GetWindowId(id);
            if (windowId != null)
            {
                AutomationElement? window = RequireUniqueOwned(
                    _application.Automation.GetDesktop().FindAllChildren(cf => cf.ByAutomationId(windowId)),
                    windowId);
                if (window == null || id == windowId)
                {
                    return window;
                }

                root = window;
            }
        }

        AutomationElement[] matches = (root ?? _application.Automation.GetDesktop())
            .FindAllDescendants(cf => cf.ByAutomationId(id));
        return RequireUniqueOwned(matches, id);
    }

    /// <summary>只映射已核实的窗口边界；未知 ID 保持原查找范围。</summary>
    /// <param name="id">目标 ID。</param>
    /// <returns>父窗口 ID；未知目标为空。</returns>
    private static string? GetWindowId(string id)
    {
        if (id.StartsWith("HardwareStatus.", StringComparison.Ordinal)) return "HardwaresConnect";
        if (id.StartsWith("RecipeNew.", StringComparison.Ordinal)) return "windowNewProduct";
        if (id.StartsWith("MessageBox.", StringComparison.Ordinal)) return "MessageBox.Dialog";
        return id == "main" ? "main" : null;
    }

    /// <summary>坚持唯一性检查，再核对 PID。</summary>
    /// <param name="matches">匹配集合。</param>
    /// <param name="id">用于诊断的 ID。</param>
    /// <returns>唯一且属于本进程的对象。</returns>
    private AutomationElement? RequireUniqueOwned(AutomationElement[] matches, string id)
    {
        if (matches.Length > 1)
        {
            throw Verify.Blocked($"AutomationId={id} 匹配多于一个对象，无法唯一定位。");
        }

        if (matches.Length == 0)
        {
            return null;
        }

        AutomationElement element = matches[0];
        if (element.Properties.ProcessId.Value != _application.ProcessId)
        {
            throw Verify.Blocked($"AutomationId={id} 不属于本轮启动的进程。");
        }

        return element;
    }

    /// <summary>必需控件缺失时如实报告自动化阻塞。</summary>
    /// <param name="id">控件 ID。</param>
    /// <param name="timeout">最长等待。</param>
    /// <param name="root">查找根。</param>
    /// <returns>唯一且属于本进程的控件。</returns>
    public AutomationElement Find(string id, TimeSpan? timeout = null, AutomationElement? root = null)
    {
        try
        {
            return UiWait.UntilNotNull(() => Optional(id, root), timeout ?? _settings.UiTimeout, "AutomationId=" + id);
        }
        catch (TimeoutException)
        {
            throw Verify.Blocked($"AutomationId={id} 在指定范围内不可访问，当前步骤自动化阻塞。");
        }
    }

    /// <summary>业务等待始终先检查进程；与定位缺失区分。</summary>
    /// <param name="condition">轮询条件。</param>
    /// <param name="description">业务描述。</param>
    /// <param name="timeout">最长等待。</param>
    public void Wait(Func<bool> condition, string description, TimeSpan? timeout = null) => UiWait.Until(
        () =>
        {
            EnsureRunning();
            return condition();
        },
        timeout ?? _settings.UiTimeout,
        description);

    /// <summary>通过原生 InvokePattern 操作可见、可用的按钮。</summary>
    /// <param name="id">按钮 ID。</param>
    /// <param name="root">查找根。</param>
    public void Invoke(string id, AutomationElement? root = null)
    {
        var timer = Stopwatch.StartNew();
        AutomationElement element = Find(id, root: root);
        RequirePattern(element.Patterns.Invoke.IsSupported, id, "Invoke");
        var pattern = element.Patterns.Invoke.Pattern;
        Wait(() => element.IsEnabled && !element.IsOffscreen, id + " 可操作");
        TimeSpan readyAt = timer.Elapsed;
        pattern.Invoke();
        LogTiming(id, "Invoke", timer, readyAt);
    }

    /// <summary>使用 ValuePattern 写入并回读（密码不回读、不记录）。</summary>
    /// <param name="id">输入框 ID。</param>
    /// <param name="value">完整值。</param>
    /// <param name="secret">是否密码等敏感值。</param>
    /// <param name="root">查找根。</param>
    public void SetValue(string id, string value, bool secret = false, AutomationElement? root = null)
    {
        var timer = Stopwatch.StartNew();
        AutomationElement element = Find(id, root: root);
        RequirePattern(element.Patterns.Value.IsSupported, id, "Value");
        var pattern = element.Patterns.Value.Pattern;
        Wait(
            () => element.IsEnabled && !element.IsOffscreen && !pattern.IsReadOnly.Value,
            id + " 可见、启用且可写");
        TimeSpan readyAt = timer.Elapsed;
        try
        {
            pattern.SetValue(value);
        }
        catch (Exception) when (secret)
        {
            throw Verify.Blocked(id + " 拒绝 UIA Value 写入，自动化阻塞；未使用键盘回退。");
        }

        LogTiming(id, "SetValue", timer, readyAt);
        if (!secret)
        {
            Wait(
                () => string.Equals(element.Patterns.Value.Pattern.Value.Value, value, StringComparison.Ordinal),
                id + " 完整回读");
        }
    }

    /// <summary>在已 ID 定位的获准组合框内按精确文字选择，并通过 SelectionPattern 回读。</summary>
    /// <param name="id">组合框 ID。</param>
    /// <param name="text">获准的精确选项文字。</param>
    /// <param name="root">查找根。</param>
    public void SelectExact(string id, string text, AutomationElement? root = null)
    {
        var timer = Stopwatch.StartNew();
        Verify.That(
            _approvedSelections.TryGetValue(id, out string? approved) && approved == text,
            id + " 或选项文字“" + text + "”不在获准的定位例外内。");
        AutomationElement combo = Find(id, root: root);
        RequirePattern(combo.Patterns.Selection.IsSupported, id, "Selection");
        Wait(() => combo.IsEnabled && !combo.IsOffscreen, id + " 可见且可选");
        if (IsSelected(combo, text))
        {
            _log($"UIA耗时：{id} 已选中；定位及回读={timer.Elapsed.TotalMilliseconds:0}ms。");
            return;
        }

        RequirePattern(combo.Patterns.ExpandCollapse.IsSupported, id, "ExpandCollapse");
        var expandPattern = combo.Patterns.ExpandCollapse.Pattern;
        TimeSpan readyAt = timer.Elapsed;
        expandPattern.Expand();
        LogTiming(id, "Expand", timer, readyAt);
        try
        {
            AutomationElement option;
            try
            {
                option = UiWait.UntilNotNull(
                    () => FindNamedSelectionItem(combo, text),
                    _settings.UiTimeout,
                    id + " 的获准精确选项 " + text);
            }
            catch (TimeoutException)
            {
                throw Verify.Blocked(id + " 中无法访问精确选项 " + text + " 或未提供 SelectionItem 模式。");
            }

            var selectPattern = option.Patterns.SelectionItem.Pattern;
            Wait(() => option.IsEnabled && !option.IsOffscreen, id + " 的获准选项可交互");
            readyAt = timer.Elapsed;
            selectPattern.Select();
            LogTiming(id, "Select", timer, readyAt);
            Wait(() => IsSelected(combo, text), id + " 选中 " + text);
        }
        finally
        {
            try
            {
                combo.Patterns.ExpandCollapse.Pattern.Collapse();
            }
            catch (Exception)
            {
                // 选择成功后下拉框可能已自动收起，收起失败不改变已回读的结论。
            }
        }
    }

    /// <summary>分开记录查找等待与 UIA 调用耗时，不冒充界面出现延迟。</summary>
    /// <param name="id">对象 ID。</param>
    /// <param name="operation">操作名。</param>
    /// <param name="timer">计时器。</param>
    /// <param name="readyAt">就绪时刻。</param>
    private void LogTiming(string id, string operation, Stopwatch timer, TimeSpan readyAt)
    {
        TimeSpan returnedAt = timer.Elapsed;
        _log($"UIA耗时：{id} {operation}；定位及就绪检查={readyAt.TotalMilliseconds:0}ms；" +
             $"UIA调用返回={(returnedAt - readyAt).TotalMilliseconds:0}ms（就绪后直接调用，无固定延时）。");
    }

    /// <summary>只读取 ID 定位的组合框选中项，用于核对参数已进入实际编辑状态。</summary>
    /// <param name="id">组合框 ID。</param>
    /// <param name="text">期望选中项文字。</param>
    /// <param name="root">查找根。</param>
    public void AssertSelected(string id, string text, AutomationElement root)
    {
        AutomationElement combo = Find(id, root: root);
        RequirePattern(combo.Patterns.Selection.IsSupported, id, "Selection");
        try
        {
            Wait(() => !combo.IsOffscreen && IsSelected(combo, text), id + " 回读为 " + text);
        }
        catch (TimeoutException)
        {
            Verify.That(
                !combo.IsOffscreen,
                id + " 在界面上不可见（IsOffscreen=True），不能作为可回读字段；请确认该控件是否被产品隐藏。");
            throw;
        }
    }

    /// <summary>控件模式缺失时停止该步骤，避免封装回退到键鼠模拟。</summary>
    /// <param name="supported">是否支持该模式。</param>
    /// <param name="id">控件 ID。</param>
    /// <param name="pattern">模式名。</param>
    public static void RequirePattern(bool supported, string id, string pattern)
    {
        if (!supported)
        {
            throw Verify.Blocked($"AutomationId={id} 未暴露 {pattern}，该步骤自动化阻塞。");
        }
    }

    /// <summary>通过 UIA 原生焦点接口触发失焦绑定。</summary>
    /// <param name="element">要聚焦的元素。</param>
    public static void Focus(AutomationElement element)
    {
        Verify.That(element.IsEnabled, $"{element.AutomationId} 当前不可操作。");
        element.FrameworkAutomationElement.SetFocus();
    }

    /// <summary>读取唯一选中项显示文字。</summary>
    /// <param name="combo">组合框。</param>
    /// <param name="text">期望文字。</param>
    /// <returns>恰好选中一项且名称一致时为真。</returns>
    private static bool IsSelected(AutomationElement combo, string text)
    {
        AutomationElement[] selected = combo.Patterns.Selection.Pattern.Selection.Value;
        if (selected.Length != 1)
        {
            return false;
        }

        AutomationElement item = selected[0];
        if (string.Equals(item.Name, text, StringComparison.Ordinal))
        {
            return true;
        }

        return item.FindAllDescendants(cf => cf.ByName(text)).Length == 1;
    }

    /// <summary>在获准组合框内按精确显示文字查找所属可选择项。</summary>
    /// <param name="parent">已按 ID 定位的组合框。</param>
    /// <param name="text">精确选项文字。</param>
    /// <returns>唯一的可选择项。</returns>
    private static AutomationElement? FindNamedSelectionItem(AutomationElement parent, string text)
    {
        var items = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        foreach (AutomationElement named in parent.FindAllDescendants(cf => cf.ByName(text)))
        {
            if (!string.Equals(named.Name, text, StringComparison.Ordinal))
            {
                continue;
            }

            AutomationElement? item = named;
            while (item != null && !item.Equals(parent) && !item.Patterns.SelectionItem.IsSupported)
            {
                item = item.Parent;
            }

            if (item == null || item.Equals(parent))
            {
                continue;
            }

            string runtimeId = string.Join(",", item.Properties.RuntimeId.Value);
            items[runtimeId] = item;
        }

        if (items.Count > 1)
        {
            throw Verify.Blocked("获准容器内精确名称 " + text + " 的可选择项不唯一。");
        }

        return items.Values.Count == 0 ? null : items.Values.First();
    }
}

/// <summary>软件用例的 AutomationId 定位：取首个匹配，不提供其他定位依据回退。</summary>
internal static class SoftwareUi
{
    /// <summary>只按 ID 等待首个匹配对象；超时缺失属于自动化阻塞。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="id">AutomationId。</param>
    /// <param name="timeout">超时。</param>
    /// <returns>找到的元素。</returns>
    internal static AutomationElement Find(AutomationElement root, string id, TimeSpan timeout)
    {
        try
        {
            return UiWait.UntilNotNull(() => FindOptional(root, id), timeout, $"AutomationId={id}");
        }
        catch (TimeoutException exception)
        {
            throw Verify.Blocked($"自动化定位阻塞：{id}。{exception.Message}");
        }
    }

    /// <summary>返回首个 ID 匹配或空值。</summary>
    /// <param name="root">查找根。</param>
    /// <param name="id">AutomationId。</param>
    /// <returns>首个匹配。</returns>
    internal static AutomationElement? FindOptional(AutomationElement root, string id) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(id));

    /// <summary>控件模式缺失时停止该步骤。</summary>
    /// <param name="supported">是否支持。</param>
    /// <param name="id">控件 ID。</param>
    /// <param name="pattern">模式。</param>
    internal static void RequirePattern(bool supported, string id, string pattern)
    {
        if (!supported)
        {
            throw Verify.Blocked($"AutomationId={id} 未暴露 {pattern}，该步骤自动化阻塞。");
        }
    }

    /// <summary>通过 UIA 原生焦点接口触发失焦绑定。</summary>
    /// <param name="element">目标元素。</param>
    internal static void Focus(AutomationElement element) => OwnedUi.Focus(element);
}
