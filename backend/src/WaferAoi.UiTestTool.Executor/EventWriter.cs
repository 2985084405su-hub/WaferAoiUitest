using System.Text;
using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 本轮唯一的事件出口。先写盘并刷新，再让 Web 通过文件增量读取转发，
/// 保证事件顺序、序号和落盘内容一致；普通控制台日志不混入协议流。
/// </summary>
internal sealed class EventWriter : IDisposable
{
    /// <summary>运行标识。</summary>
    private readonly string _runId;

    /// <summary>只追加的事件文件。</summary>
    private readonly StreamWriter _writer;

    /// <summary>保护序号和写入。</summary>
    private readonly object _sync = new();

    /// <summary>最后写入的序号。</summary>
    private long _sequence;

    /// <summary>创建事件写入器。</summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="path">events.jsonl 绝对路径。</param>
    public EventWriter(string runId, string path)
    {
        _runId = runId;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>写入一条事件并返回它。</summary>
    /// <param name="type">事件类型。</param>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="phase">阶段。</param>
    /// <param name="status">步骤状态。</param>
    /// <param name="reason">原因。</param>
    /// <param name="checkpointId">确认点标识。</param>
    /// <param name="message">日志或说明。</param>
    /// <param name="data">附加文本键值。</param>
    /// <returns>已写入的事件。</returns>
    public RunEvent Emit(
        EventType type,
        string? stepId = null,
        RunPhase? phase = null,
        StepStatus? status = null,
        string? reason = null,
        string? checkpointId = null,
        string? message = null,
        Dictionary<string, string>? data = null)
    {
        var item = new RunEvent
        {
            RunId = _runId,
            At = DateTimeOffset.Now,
            Type = type,
            StepId = stepId,
            Phase = phase,
            Status = status,
            Reason = reason,
            CheckpointId = checkpointId,
            Message = message,
            Data = data
        };
        Append(item);
        return item;
    }

    /// <summary>
    /// 登记一个已落盘证据并写入 ArtifactCreated 事件。
    /// </summary>
    /// <param name="path">证据文件绝对路径。</param>
    /// <param name="kind">类别。</param>
    /// <param name="mimeType">MIME 类型。</param>
    /// <param name="stepId">关联步骤；为空表示本轮级。</param>
    /// <param name="reason">登记原因。</param>
    /// <returns>证据 ID；文件不存在时返回 null，不伪造证据。</returns>
    public string? RegisterArtifact(
        string path,
        string kind,
        string mimeType,
        string? stepId = null,
        string? reason = null)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string artifactId = Guid.NewGuid().ToString("N");
        var item = new RunEvent
        {
            RunId = _runId,
            At = DateTimeOffset.Now,
            Type = EventType.ArtifactCreated,
            StepId = stepId,
            ArtifactId = artifactId,
            ArtifactName = Path.GetFileName(path),
            ArtifactKind = kind,
            MimeType = mimeType,
            ArtifactSize = new FileInfo(path).Length,
            Reason = reason
        };
        Append(item);
        return artifactId;
    }

    /// <summary>追加并刷新一条事件。</summary>
    /// <param name="item">待写入事件。</param>
    private void Append(RunEvent item)
    {
        lock (_sync)
        {
            item.Sequence = ++_sequence;
            _writer.WriteLine(JsonSerializer.Serialize(item, JsonDefaults.Executor));
            _writer.Flush();
        }
    }

    /// <summary>关闭事件文件。</summary>
    public void Dispose() => _writer.Dispose();
}
