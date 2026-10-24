using System.Diagnostics;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 本轮诊断与证据。日志一行一条写入 steps.log；截图、UIA 树、进程状态、异常都落到本轮
/// artifacts 目录并通过 ArtifactCreated 事件登记，供网页按 artifactId 取回。
/// 与源工程 ArtifactCollector 的差别只有登记出口：TestContext.AddResultFile → 结构化事件。
/// </summary>
internal sealed class ExecutorArtifacts
{
    /// <summary>单个 UIA 树转储的元素上限，防止超大窗口拖垮执行器。</summary>
    private const int MaximumTreeElements = 20_000;

    /// <summary>本轮目录。</summary>
    private readonly string _runDirectory;

    /// <summary>证据目录。</summary>
    private readonly string _artifactDirectory;

    /// <summary>只追加的步骤日志。</summary>
    private readonly string _stepLogPath;

    /// <summary>事件出口。</summary>
    private readonly EventWriter _writer;

    /// <summary>日志写入同步。</summary>
    private readonly object _logSync = new();

    /// <summary>已登记文件，避免重复登记同一证据。</summary>
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>序号，用于同名截图不覆盖。</summary>
    private int _sequence;

    /// <summary>创建证据收集器。</summary>
    /// <param name="runDirectory">本轮目录。</param>
    /// <param name="writer">事件出口。</param>
    public ExecutorArtifacts(string runDirectory, EventWriter writer)
    {
        _runDirectory = runDirectory;
        _artifactDirectory = Path.Combine(runDirectory, "artifacts");
        Directory.CreateDirectory(_artifactDirectory);
        _writer = writer;
        _stepLogPath = Path.Combine(_artifactDirectory, "steps.log");
        File.WriteAllText(_stepLogPath, string.Empty, new UTF8Encoding(false));
    }

    /// <summary>证据目录绝对路径。</summary>
    public string ArtifactDirectory => _artifactDirectory;

    /// <summary>步骤日志绝对路径。</summary>
    public string StepLogPath => _stepLogPath;

    /// <summary>写一行步骤日志，并作为 Log 事件进入协议流。</summary>
    /// <param name="message">不含密码的业务日志。</param>
    public void Log(string message)
    {
        string line = $"{DateTimeOffset.Now:O} {message}";
        lock (_logSync)
        {
            File.AppendAllText(_stepLogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        _writer.Emit(EventType.Log, message: line);
    }

    /// <summary>
    /// 采集当前主屏截图并登记。截图失败只记录诊断，不改变步骤结论。
    /// </summary>
    /// <param name="name">业务可读名称，例如“07.18角度结果”。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? CaptureScreen(string name, string? stepId = null) =>
        TryRegister(name, stepId, "screenshot", "image/png", () =>
            Capture.MainScreen().ToFile(PathFor(name, "png")));

    /// <summary>
    /// 采集指定 UIA 元素的截图并登记。
    /// </summary>
    /// <param name="element">要采集的元素。</param>
    /// <param name="name">业务可读名称。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? CaptureElement(AutomationElement element, string name, string? stepId = null) =>
        TryRegister(name, stepId, "screenshot", "image/png", () =>
            Capture.Element(element).ToFile(PathFor(name, "png")));

    /// <summary>
    /// 把被测程序全部顶层窗口的 UIA 树转储为文本并登记，用于缺 ID 控件的现场定位。
    /// </summary>
    /// <param name="application">程序驱动；未启动时记录原因。</param>
    /// <param name="name">业务可读名称。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? CaptureAutomationTree(WaferAoiApplication? application, string name, string? stepId = null) =>
        TryRegister(name, stepId, "tree", "text/plain", () =>
        {
            string path = PathFor(name, "txt");
            File.WriteAllText(path, BuildAutomationTree(application), new UTF8Encoding(false));
        });

    /// <summary>
    /// 采集进程归属与存活状态并登记。
    /// </summary>
    /// <param name="application">程序驱动。</param>
    /// <param name="name">业务可读名称。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? CaptureProcessState(WaferAoiApplication? application, string name, string? stepId = null) =>
        TryRegister(name, stepId, "process", "text/plain", () =>
        {
            string path = PathFor(name, "txt");
            File.WriteAllText(path, BuildProcessState(application), new UTF8Encoding(false));
        });

    /// <summary>
    /// 写文本证据并登记。
    /// </summary>
    /// <param name="name">业务可读名称。</param>
    /// <param name="content">内容。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <param name="kind">证据类别。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? WriteText(string name, string content, string? stepId, string kind = "text")
    {
        string path = PathFor(name, "txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return Register(path, kind, "text/plain", stepId);
    }

    /// <summary>
    /// 失败时集中采集原始证据：异常、主屏、窗口清单、UIA 树、进程状态、步骤日志。
    /// 每项独立执行，一项失败不影响其余项，也不掩盖原始断言结论。
    /// </summary>
    /// <param name="application">程序驱动，可能尚未启动。</param>
    /// <param name="exception">原始异常。</param>
    /// <param name="stepId">失败步骤。</param>
    public void CollectFailure(WaferAoiApplication? application, Exception exception, string? stepId)
    {
        WriteText("failure-exception", exception.ToString(), stepId, "exception");
        CaptureScreen("failure-screen", stepId);
        WriteText("failure-windows", BuildWindowList(application), stepId);
        CaptureAutomationTree(application, "failure-automation-tree", stepId);
        CaptureProcessState(application, "failure-process", stepId);
        RegisterIfExists(_stepLogPath, "log", "text/plain", stepId, "步骤日志");
    }

    /// <summary>登记步骤日志（结束时再次登记，保证网页可见最新大小）。</summary>
    /// <param name="stepId">关联步骤；本轮级证据传 null。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    public string? RegisterStepLog(string? stepId) =>
        RegisterIfExists(_stepLogPath, "log", "text/plain", stepId, "步骤日志");

    /// <summary>把证据文件登记为事件；文件不存在时不伪造证据。</summary>
    /// <param name="path">证据绝对路径。</param>
    /// <param name="kind">类别。</param>
    /// <param name="mimeType">MIME 类型。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <param name="reason">登记原因。</param>
    /// <returns>证据 ID；文件不存在时为 null。</returns>
    public string? Register(string path, string kind, string mimeType, string? stepId, string? reason = null)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string? artifactId = _writer.RegisterArtifact(path, kind, mimeType, stepId, reason);
        if (artifactId != null)
        {
            lock (_logSync)
            {
                _registered.Add(path);
            }
        }

        return artifactId;
    }

    /// <summary>按需登记已存在文件，避免同一路径返回空 ID 时反复尝试。</summary>
    /// <param name="path">证据路径。</param>
    /// <param name="kind">类别。</param>
    /// <param name="mimeType">MIME 类型。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <param name="reason">登记原因。</param>
    /// <returns>证据 ID。</returns>
    private string? RegisterIfExists(string path, string kind, string mimeType, string? stepId, string? reason) =>
        File.Exists(path) ? Register(path, kind, mimeType, stepId, reason) : null;

    /// <summary>生成不重复的证据文件路径。</summary>
    /// <param name="name">业务可读名称。</param>
    /// <param name="extension">扩展名，不含点。</param>
    /// <returns>绝对路径。</returns>
    private string PathFor(string name, string extension)
    {
        int sequence = Interlocked.Increment(ref _sequence);
        string safe = new string(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        return Path.Combine(_artifactDirectory, $"{sequence:000}-{safe}.{extension}");
    }

    /// <summary>采集一项证据；失败时写错误文本，不抛出。</summary>
    /// <param name="name">业务可读名称。</param>
    /// <param name="stepId">关联步骤。</param>
    /// <param name="kind">类别。</param>
    /// <param name="mimeType">MIME 类型。</param>
    /// <param name="capture">采集动作。</param>
    /// <returns>证据 ID；失败时为 null。</returns>
    private string? TryRegister(string name, string? stepId, string kind, string mimeType, Action capture)
    {
        string path = PathFor(name, mimeType == "image/png" ? "png" : "txt");
        try
        {
            capture();
            return Register(path, kind, mimeType, stepId, name) ?? path;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    path + ".error.txt",
                    $"证据采集失败：{exception.GetType().Name}: {exception.Message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 连错误文本都写不了时保持静默；失败证据缺失不应覆盖步骤结论。
            }

            Log($"证据采集失败（{name}）：{exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>构造 UIA 树文本。</summary>
    /// <param name="application">程序驱动。</param>
    /// <returns>树文本。</returns>
    private static string BuildAutomationTree(WaferAoiApplication? application)
    {
        if (application == null || application.ProcessId == 0)
        {
            return "被测程序尚未启动。";
        }

        var builder = new StringBuilder();
        int count = 0;
        foreach (Window window in application.GetTopLevelWindows())
        {
            AppendElement(builder, window, 0, ref count);
            if (count >= MaximumTreeElements)
            {
                builder.AppendLine($"已达 {MaximumTreeElements} 元素上限，转储截断。");
                break;
            }
        }

        return builder.Length == 0 ? "没有可读取的顶层窗口。" : builder.ToString();
    }

    /// <summary>递归追加 UIA 子树，容忍 WPF 失效 peer。</summary>
    /// <param name="builder">输出。</param>
    /// <param name="element">当前元素。</param>
    /// <param name="depth">深度。</param>
    /// <param name="count">已输出元素数。</param>
    private static void AppendElement(StringBuilder builder, AutomationElement element, int depth, ref int count)
    {
        if (count >= MaximumTreeElements)
        {
            return;
        }

        count++;
        try
        {
            builder.Append(' ', Math.Min(depth, 60) * 2);
            builder.Append("type=").Append(element.ControlType);
            builder.Append("; name='").Append(Escape(element.Name)).Append('\'');
            builder.Append("; id='").Append(Escape(element.AutomationId)).Append('\'');
            builder.Append("; enabled=").Append(element.IsEnabled);
            builder.Append("; offscreen=").Append(element.Properties.IsOffscreen.ValueOrDefault);
            if (element.Properties.BoundingRectangle.ValueOrDefault is { IsEmpty: false } rect)
            {
                builder.Append("; rect=").Append($"{rect.X},{rect.Y},{rect.Width},{rect.Height}");
            }

            builder.AppendLine();
            foreach (AutomationElement child in element.FindAllChildren())
            {
                AppendElement(builder, child, depth + 1, ref count);
                if (count >= MaximumTreeElements)
                {
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            builder.Append(' ', Math.Min(depth + 1, 60) * 2);
            builder.AppendLine($"<UIA 读取失败：{exception.GetType().Name}: {exception.Message}>");
        }
    }

    /// <summary>构造窗口清单文本。</summary>
    /// <param name="application">程序驱动。</param>
    /// <returns>文本。</returns>
    private static string BuildWindowList(WaferAoiApplication? application)
    {
        if (application == null || application.ProcessId == 0)
        {
            return "被测程序尚未启动。";
        }

        Window[] windows = application.GetTopLevelWindows();
        var lines = new List<string> { $"WindowCount={windows.Length}" };
        for (int index = 0; index < windows.Length; index++)
        {
            Window window = windows[index];
            lines.Add(
                $"Window[{index}]: Title='{Escape(window.Title)}'; Id='{Escape(window.AutomationId)}'; " +
                $"ControlType={window.ControlType}; Enabled={window.IsEnabled}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>构造进程状态文本。</summary>
    /// <param name="application">程序驱动。</param>
    /// <returns>文本。</returns>
    private static string BuildProcessState(WaferAoiApplication? application)
    {
        if (application == null || application.ProcessId == 0)
        {
            return "被测程序尚未启动。";
        }

        var lines = new List<string>
        {
            $"ProcessId={application.ProcessId}",
            $"IsRunning={application.IsRunning}"
        };
        try
        {
            using Process process = Process.GetProcessById(application.ProcessId);
            lines.Add($"ProcessName={process.ProcessName}");
            lines.Add($"StartTime={process.StartTime:O}");
            lines.Add($"Responding={process.Responding}");
            lines.Add($"MainWindowTitle={process.MainWindowTitle}");
        }
        catch (Exception exception)
        {
            lines.Add($"ProcessReadError={exception.GetType().Name}: {exception.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>转义换行，保证一个元素一行。</summary>
    /// <param name="value">原始值。</param>
    /// <returns>单行文本。</returns>
    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
}
