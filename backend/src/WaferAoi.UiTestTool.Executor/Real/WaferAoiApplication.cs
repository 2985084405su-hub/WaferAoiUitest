using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 拥有本轮被测进程及其 UIA3 会话。只有本类启动的 PID 会被操作或终止；
/// 迁移自源工程 Drivers/WaferAoiApplication.cs。
/// </summary>
internal sealed class WaferAoiApplication : IDisposable
{
    /// <summary>本轮配置。</summary>
    private readonly ExecutorSettings _settings;

    /// <summary>步骤日志。</summary>
    private readonly Action<string> _log;

    /// <summary>FlaUI 进程封装；只有本类启动后才非空。</summary>
    private Application? _application;

    /// <summary>UIA3 会话。</summary>
    private UIA3Automation? _automation;

    /// <summary>创建程序驱动，不启动进程。</summary>
    /// <param name="settings">本轮配置。</param>
    /// <param name="log">步骤日志。</param>
    public WaferAoiApplication(ExecutorSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>UIA3 会话。</summary>
    public UIA3Automation Automation =>
        _automation ?? throw new InvalidOperationException("UIA3 会话尚未启动。");

    /// <summary>本轮启动的 PID，未启动为 0。</summary>
    public int ProcessId => _application?.ProcessId ?? 0;

    /// <summary>本轮启动时间，用于核对 PID 未被复用。</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>本轮进程是否仍在运行。</summary>
    public bool IsRunning
    {
        get
        {
            if (_application == null)
            {
                return false;
            }

            try
            {
                return !Process.GetProcessById(_application.ProcessId).HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>启动被测程序并从其目录工作。</summary>
    public void Start()
    {
        if (_application != null)
        {
            throw new InvalidOperationException("程序驱动只能启动一个进程。");
        }

        _log($"启动被测程序：{_settings.ApplicationPath}");
        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.ApplicationPath,
            WorkingDirectory = Path.GetDirectoryName(_settings.ApplicationPath) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = true
        };
        _automation = new UIA3Automation();
        _application = Application.Launch(startInfo);
        StartedAt = DateTimeOffset.Now;
        _log($"被测程序已启动。PID={_application.ProcessId}");
    }

    /// <summary>立即终止仅由本类启动的进程。</summary>
    public void Kill()
    {
        if (_application == null)
        {
            return;
        }

        int processId = _application.ProcessId;
        if (IsRunning)
        {
            _log($"终止本轮被测程序。PID={processId}");
            _application.Kill();
        }

        _log($"本轮被测程序已停止。PID={processId}");
    }

    /// <summary>返回本轮进程当前的顶层窗口快照。</summary>
    /// <returns>窗口数组。</returns>
    public Window[] GetTopLevelWindows()
    {
        if (_application == null || _automation == null)
        {
            return Array.Empty<Window>();
        }

        try
        {
            using Process process = Process.GetProcessById(_application.ProcessId);
            if (process.HasExited || !process.Responding)
            {
                // 启动初始化期间 UI 线程忙，深入遍历 UIA 树会阻塞测试线程。
                return Array.Empty<Window>();
            }
        }
        catch (ArgumentException)
        {
            return Array.Empty<Window>();
        }

        return _application.GetAllTopLevelWindows(_automation);
    }

    /// <summary>释放 FlaUI 资源；调用前由业务流程决定是否终止进程。</summary>
    public void Dispose()
    {
        _application?.Dispose();
        _application = null;
        _automation?.Dispose();
        _automation = null;
    }
}
