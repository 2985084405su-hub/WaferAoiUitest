using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 一条已提交的网页确认命令。Web 先写临时文件再原子改名，执行器只读取完整 JSON。
/// </summary>
internal sealed class CommandEnvelope
{
    /// <summary>请求标识。</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>运行标识。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>确认点标识。</summary>
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>现场结果。</summary>
    public CheckpointResult Result { get; set; }

    /// <summary>阻塞或失败原因。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>现场备注。</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// 执行器侧命令通道：轮询本轮 commands 目录，处理后把回执写到 acks 目录。
/// 只接受带 runId、checkpointId 与 requestId 的命令，不提供任意命令或脚本入口。
/// </summary>
internal sealed class CommandChannel : IDisposable
{
    /// <summary>运行标识。</summary>
    private readonly string _runId;

    /// <summary>命令目录。</summary>
    private readonly string _commandDirectory;

    /// <summary>回执目录。</summary>
    private readonly string _ackDirectory;

    /// <summary>已处理请求 → 对应确认点，保证重复提交幂等且不会跨确认点生效。</summary>
    private readonly Dictionary<string, string> _processed = new(StringComparer.Ordinal);

    /// <summary>创建命令通道。</summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="commandDirectory">命令目录。</param>
    /// <param name="ackDirectory">回执目录。</param>
    public CommandChannel(string runId, string commandDirectory, string ackDirectory)
    {
        _runId = runId;
        _commandDirectory = commandDirectory;
        _ackDirectory = ackDirectory;
        Directory.CreateDirectory(_commandDirectory);
        Directory.CreateDirectory(_ackDirectory);
    }

    /// <summary>
    /// 尝试读取指定确认点的第一条命令；无关确认点的命令只回执拒绝，不改变等待状态。
    /// </summary>
    /// <param name="checkpointId">当前确认点。</param>
    /// <returns>匹配的命令；没有时返回 null。</returns>
    public CommandEnvelope? TryRead(string checkpointId)
    {
        if (!Directory.Exists(_commandDirectory))
        {
            return null;
        }

        foreach (string path in Directory.EnumerateFiles(_commandDirectory, "*.json").OrderBy(item => item))
        {
            CommandEnvelope? command;
            try
            {
                command = JsonSerializer.Deserialize<CommandEnvelope>(File.ReadAllText(path), JsonDefaults.Executor);
            }
            catch (Exception exception)
            {
                WriteAck(Path.GetFileNameWithoutExtension(path), false, "invalid_command", exception.Message, true);
                continue;
            }

            if (command == null || string.IsNullOrWhiteSpace(command.RequestId))
            {
                continue;
            }

            if (_processed.TryGetValue(command.RequestId, out string? processedCheckpoint))
            {
                bool sameCheckpoint = string.Equals(processedCheckpoint, checkpointId, StringComparison.Ordinal);
                WriteAck(
                    command.RequestId,
                    sameCheckpoint,
                    sameCheckpoint ? "replayed" : "checkpoint_not_current",
                    sameCheckpoint ? "同一请求已处理。" : "该请求已用于其他确认点，不能再次生效。",
                    sameCheckpoint);
                continue;
            }

            if (!string.Equals(command.RunId, _runId, StringComparison.Ordinal) ||
                !string.Equals(command.CheckpointId, checkpointId, StringComparison.Ordinal))
            {
                _processed[command.RequestId] = command.CheckpointId;
                WriteAck(
                    command.RequestId,
                    false,
                    "checkpoint_not_current",
                    "该命令不属于当前等待中的确认点，已拒绝。",
                    false);
                continue;
            }

            _processed[command.RequestId] = checkpointId;
            return command;
        }

        return null;
    }

    /// <summary>写回执。</summary>
    /// <param name="requestId">请求标识。</param>
    /// <param name="accepted">是否接受。</param>
    /// <param name="code">结果码。</param>
    /// <param name="message">说明。</param>
    /// <param name="applied">执行器是否已应用。</param>
    public void WriteAck(string requestId, bool accepted, string code, string message, bool applied)
    {
        var payload = new
        {
            requestId,
            accepted,
            code,
            message,
            applied,
            at = DateTimeOffset.Now
        };
        string path = Path.Combine(_ackDirectory, SafeName(requestId) + ".json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonDefaults.Executor));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporary, path);
    }

    /// <summary>把请求标识转换为安全文件名。</summary>
    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    /// <summary>释放通道；当前没有持久连接。</summary>
    public void Dispose()
    {
    }
}
