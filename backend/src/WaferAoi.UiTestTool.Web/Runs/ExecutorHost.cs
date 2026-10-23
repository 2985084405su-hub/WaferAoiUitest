using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;
using WaferAoi.UiTestTool.Web.Storage;

namespace WaferAoi.UiTestTool.Web.Runs;

/// <summary>
/// 启动并监视桌面执行器。执行器独占写 events.jsonl，宿主只按增量读取；
/// 控制台输出另存为诊断文件，不与协议流混用。
/// </summary>
internal sealed class ExecutorHost
{
    /// <summary>日志。</summary>
    private readonly ILogger<ExecutorHost> _logger;

    /// <summary>创建宿主。</summary>
    /// <param name="logger">日志。</param>
    public ExecutorHost(ILogger<ExecutorHost> logger) => _logger = logger;

    /// <summary>
    /// 启动执行器、转发新事件，并在进程退出后完成事件排空。
    /// </summary>
    /// <param name="session">本轮会话。</param>
    /// <param name="executorPath">执行器路径。</param>
    /// <param name="simulate">是否模拟。</param>
    /// <param name="scenario">模拟场景。</param>
    /// <param name="onEvent">事件回调。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>执行器退出码；无法取得时为 null。</returns>
    public async Task<int?> RunAsync(
        RunSession session,
        string executorPath,
        bool simulate,
        string? scenario,
        Func<RunEvent, Task> onEvent,
        Action<int>? onStarted,
        CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(session.RunDirectory, "manifest.json");
        string eventsPath = Path.Combine(session.RunDirectory, "events.jsonl");
        string arguments = $"--manifest \"{manifestPath}\"";
        if (simulate)
        {
            arguments += " --simulate";
        }

        if (!string.IsNullOrWhiteSpace(scenario))
        {
            arguments += $" --scenario \"{scenario}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executorPath,
            Arguments = arguments,
            WorkingDirectory = session.RunDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动执行器：{executorPath}");
        }

        session.Snapshot.Executor.ProcessId = process.Id;
        session.Snapshot.Executor.StartedAt = DateTimeOffset.Now;
        onStarted?.Invoke(process.Id);
        session.Snapshot.Executor.Alive = true;

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        long consumed = 0;
        try
        {
            while (true)
            {
                consumed = await DrainAsync(eventsPath, consumed, session.Snapshot.RunId, onEvent, cancellationToken)
                    .ConfigureAwait(false);

                if (process.HasExited)
                {
                    // 进程退出后再读一次，防止最后几行尚未被监视循环看到。
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    consumed = await DrainAsync(eventsPath, consumed, session.Snapshot.RunId, onEvent, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }

                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }

            int exitCode = process.ExitCode;
            await WriteDiagnosticsAsync(session, stdout, stderr, exitCode).ConfigureAwait(false);
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "停止执行器失败：{RunId}", session.Snapshot.RunId);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 接管仍在运行的执行器：只读取事件并等待进程退出，不启动新进程、不重跑步骤。
    /// </summary>
    /// <param name="session">本轮会话。</param>
    /// <param name="processId">执行器 PID。</param>
    /// <param name="onEvent">事件回调。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task AttachAsync(
        RunSession session,
        int processId,
        Func<RunEvent, Task> onEvent,
        Action<int>? onStarted,
        CancellationToken cancellationToken)
    {
        string eventsPath = Path.Combine(session.RunDirectory, "events.jsonl");
        long consumed = 0;
        while (true)
        {
            consumed = await DrainAsync(eventsPath, consumed, session.Snapshot.RunId, onEvent, cancellationToken)
                .ConfigureAwait(false);

            bool alive;
            try
            {
                using Process process = Process.GetProcessById(processId);
                alive = !process.HasExited;
            }
            catch (Exception)
            {
                alive = false;
            }

            if (!alive)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                await DrainAsync(eventsPath, consumed, session.Snapshot.RunId, onEvent, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>读取新完成的 JSONL 行。</summary>
    /// <summary>
    /// 以共享读写方式读取事件文件。执行器持有写句柄，普通 ReadAllLines 的 FileShare.Read
    /// 会与写句柄冲突，导致宿主在运行期间永远读不到事件。
    /// </summary>
    /// <param name="path">事件文件。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>完整行。</returns>
    private static async Task<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        // 绌烘枃浠朵細寰楀埌涓€涓┖鍏冪礌锛涜嫢涓嶅墧闄わ紝璇诲彇娓告爣浼氳秺杩囧皻鏈啓鍏ョ殑绗竴鏉′簨浠躲€?        if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static async Task<long> DrainAsync(
        string eventsPath,
        long consumed,
        string runId,
        Func<RunEvent, Task> onEvent,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(eventsPath))
        {
            return consumed;
        }

        string[] lines;
        try
        {
            lines = await ReadAllLinesSharedAsync(eventsPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return consumed;
        }

        long next = consumed;
        for (long index = consumed; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                next = index + 1;
                continue;
            }

            RunEvent? item;
            try
            {
                item = JsonSerializer.Deserialize<RunEvent>(lines[index], JsonDefaults.File);
            }
            catch (JsonException)
            {
                // 可能是执行器正在写的半条记录；本轮不推进读取位置，下一轮重试。
                break;
            }

            next = index + 1;
            if (item == null || !string.Equals(item.RunId, runId, StringComparison.Ordinal))
            {
                continue;
            }

            await onEvent(item).ConfigureAwait(false);
        }

        return next;
    }

    /// <summary>把执行器控制台输出写入诊断文件。</summary>
    private static async Task WriteDiagnosticsAsync(
        RunSession session,
        Task<string> stdout,
        Task<string> stderr,
        int exitCode)
    {
        string output = await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        File.WriteAllText(
            Path.Combine(session.RunDirectory, "executor.stdout.log"),
            output + Environment.NewLine + $"exitCode={exitCode}" + Environment.NewLine);
        File.WriteAllText(Path.Combine(session.RunDirectory, "executor.stderr.log"), error);
    }
}
