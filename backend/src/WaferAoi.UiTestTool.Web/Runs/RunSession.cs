using System.Threading.Channels;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Web.Runs;

/// <summary>单轮运行的内存会话；事件顺序以 events.jsonl 为准。</summary>
internal sealed class RunSession
{
    /// <summary>同步对象。</summary>
    public object Sync { get; } = new();

    /// <summary>本轮清单。</summary>
    public RunManifest Manifest { get; set; } = new();

    /// <summary>运行快照。</summary>
    public RunSnapshot Snapshot { get; set; } = new();

    /// <summary>已加载事件；用于 SSE 补流。</summary>
    public List<RunEvent> Events { get; } = new();

    /// <summary>SSE 订阅中心。</summary>
    public EventHub Hub { get; } = new();

    /// <summary>确认请求 → 已受理响应，保证同一 requestId 幂等。</summary>
    public Dictionary<string, ConfirmCheckpointResponse> ConfirmReplies { get; } = new(StringComparer.Ordinal);

    /// <summary>确认点 → 已受理的请求标识；同一确认点只允许一个待回执命令。</summary>
    public Dictionary<string, string> CheckpointRequests { get; } = new(StringComparer.Ordinal);

    /// <summary>已收敛。</summary>
    public bool Finished { get; set; }

    /// <summary>执行器监视线程。</summary>
    public Task? MonitorTask { get; set; }

    /// <summary>本轮目录。</summary>
    public string RunDirectory { get; set; } = string.Empty;

    /// <summary>是否处于待现场处理。</summary>
    public bool PendingFieldActive => Snapshot.PendingField.Active;

    /// <summary>最近事件序号。</summary>
    public long LastSequence
    {
        get
        {
            lock (Sync)
            {
                return Snapshot.LastSequence;
            }
        }
    }
}

/// <summary>
/// 实时事件广播。慢客户端只丢弃旧事件，不阻塞执行器或事件落盘；
/// 重连时客户端按 sequence 补流，因此丢弃不会造成最终状态缺失。
/// </summary>
internal sealed class EventHub
{
    /// <summary>同步对象。</summary>
    private readonly object _sync = new();

    /// <summary>当前订阅者。</summary>
    private readonly List<Channel<RunEvent>> _subscribers = new();

    /// <summary>订阅事件；返回读取端。</summary>
    /// <returns>事件读取端。</returns>
    public ChannelReader<RunEvent> Subscribe()
    {
        Channel<RunEvent> channel = Channel.CreateBounded<RunEvent>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        lock (_sync)
        {
            _subscribers.Add(channel);
        }

        return channel.Reader;
    }

    /// <summary>发布事件。</summary>
    /// <param name="item">事件。</param>
    public void Publish(RunEvent item)
    {
        lock (_sync)
        {
            foreach (Channel<RunEvent> subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(item);
            }
        }
    }

    /// <summary>结束全部订阅；仅在本轮长期不再变化时使用。</summary>
    public void Complete()
    {
        lock (_sync)
        {
            foreach (Channel<RunEvent> subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }
}
