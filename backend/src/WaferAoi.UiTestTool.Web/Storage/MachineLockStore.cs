using System.Text.Json;

namespace WaferAoi.UiTestTool.Web.Storage;

/// <summary>
/// 机器占用状态。待现场处理与进程身份跨宿主重启保留；只有现场处理核验通过才删除。
/// </summary>
internal sealed class MachineLockState
{
    /// <summary>占用本机的运行。</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>占用开始时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最近阶段。</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>是否待现场处理。</summary>
    public bool PendingField { get; set; }

    /// <summary>执行器 PID；未知为 0。</summary>
    public int ExecutorPid { get; set; }

    /// <summary>本轮被测程序 PID；未知为 0。</summary>
    public int ProductProcessId { get; set; }

    /// <summary>被测程序启动时间，用于核对 PID 未被复用。</summary>
    public DateTimeOffset? ProductProcessStartedAt { get; set; }

    /// <summary>占用或待处理原因。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>最近更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>机器锁文件读写。</summary>
internal sealed class MachineLockStore
{
    /// <summary>同步锁。</summary>
    private readonly object _sync = new();

    /// <summary>路径约定。</summary>
    private readonly LocalPaths _paths;

    /// <summary>创建存储。</summary>
    /// <param name="paths">路径约定。</param>
    public MachineLockStore(LocalPaths paths) => _paths = paths;

    /// <summary>读取锁；不存在或损坏时返回 null。</summary>
    /// <returns>锁状态。</returns>
    public MachineLockState? Read()
    {
        lock (_sync)
        {
            if (!File.Exists(_paths.LockPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<MachineLockState>(
                    File.ReadAllText(_paths.LockPath),
                    JsonDefaults.File);
            }
            catch (Exception)
            {
                // 锁文件损坏时不能当作“无锁”，否则可能启动第二轮；调用方按占用处理。
                return new MachineLockState
                {
                    RunId = string.Empty,
                    Reason = "机器锁文件无法解析，需人工核对。",
                    UpdatedAt = DateTimeOffset.Now
                };
            }
        }
    }

    /// <summary>写入或更新锁。</summary>
    /// <param name="state">锁状态。</param>
    public void Write(MachineLockState state)
    {
        lock (_sync)
        {
            state.UpdatedAt = DateTimeOffset.Now;
            string temporary = _paths.LockPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonDefaults.File));
            if (File.Exists(_paths.LockPath))
            {
                File.Delete(_paths.LockPath);
            }

            File.Move(temporary, _paths.LockPath);
        }
    }

    /// <summary>解除占用。</summary>
    /// <summary>尝试一次性取得机器锁；多宿主同时启动时只有一个能成功。</summary>
    /// <param name="state">锁状态。</param>
    /// <returns>取得锁时为 true。</returns>
    public bool TryAcquire(MachineLockState state)
    {
        lock (_sync)
        {
            if (File.Exists(_paths.LockPath))
            {
                return false;
            }

            state.UpdatedAt = DateTimeOffset.Now;
            string temporary = _paths.LockPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonDefaults.File));
            try
            {
                File.Move(temporary, _paths.LockPath);
                return true;
            }
            catch (IOException)
            {
                File.Delete(temporary);
                return false;
            }
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            if (File.Exists(_paths.LockPath))
            {
                File.Delete(_paths.LockPath);
            }
        }
    }
}
