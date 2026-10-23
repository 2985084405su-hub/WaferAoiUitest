using System.Text;
using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;
using WaferAoi.UiTestTool.Web.Storage;

namespace WaferAoi.UiTestTool.Web.Runs;

/// <summary>
/// 本轮记录存储。每轮一个目录，事件先落盘，快照由事件重放得到；
/// 报告引用同一快照，不另算一套结论。
/// </summary>
internal sealed class RunStore
{
    /// <summary>路径约定。</summary>
    private readonly LocalPaths _paths;

    /// <summary>日志。</summary>
    private readonly ILogger<RunStore> _logger;

    /// <summary>创建存储。</summary>
    /// <param name="paths">路径约定。</param>
    /// <param name="logger">日志。</param>
    public RunStore(LocalPaths paths, ILogger<RunStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>创建本轮目录、清单和初始快照。</summary>
    /// <param name="manifest">执行清单。</param>
    /// <returns>新会话。</returns>
    public RunSession Create(RunManifest manifest)
    {
        string runDirectory = _paths.RunDirectory(manifest.RunId);
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(Path.Combine(runDirectory, "artifacts"));
        Directory.CreateDirectory(Path.Combine(runDirectory, "commands"));
        Directory.CreateDirectory(Path.Combine(runDirectory, "acks"));

        manifest.RunDir = runDirectory;
        File.WriteAllText(
            Path.Combine(runDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.File),
            new UTF8Encoding(false));

        var session = new RunSession
        {
            Manifest = manifest,
            RunDirectory = runDirectory,
            Snapshot = new RunSnapshot
            {
                RunId = manifest.RunId,
                CreatedAt = manifest.CreatedAt,
                UpdatedAt = manifest.CreatedAt,
                Phase = RunPhase.Created,
                Conclusion = RunConclusion.Pending,
                Options = manifest.Options,
                Config = manifest.Config,
                Simulated = manifest.Simulation,
                Steps = manifest.Steps.Select(definition => new StepState
                {
                    StepId = definition.StepId,
                    Title = definition.Title,
                    Section = definition.Section,
                    SectionTitle = definition.SectionTitle,
                    Status = StepStatus.NotRun,
                    ManualRequirement = definition.ManualRequirement,
                    HardwareRelated = definition.HardwareRelated,
                    VersionAssertion = definition.VersionAssertion
                }).ToList()
            }
        };
        session.Snapshot.Counts = new StepCounts
        {
            Total = session.Snapshot.Steps.Count,
            NotRun = session.Snapshot.Steps.Count
        };
        return session;
    }

    /// <summary>读取本轮清单；目录不存在或文件损坏时返回 null。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>清单。</returns>
    public RunManifest? ReadManifest(string runId)
    {
        string path = _paths.ManifestPath(runId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(path), JsonDefaults.File);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "运行清单损坏：{RunId}", runId);
            return null;
        }
    }

    /// <summary>加载本轮会话并重放全部完整事件。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>会话；不存在时返回 null。</returns>
    public RunSession? Load(string runId)
    {
        RunManifest? manifest = ReadManifest(runId);
        if (manifest == null)
        {
            return null;
        }

        string runDirectory = _paths.RunDirectory(runId);
        manifest.RunDir = runDirectory;
        var session = new RunSession
        {
            Manifest = manifest,
            RunDirectory = runDirectory,
            Snapshot = new RunSnapshot
            {
                RunId = manifest.RunId,
                CreatedAt = manifest.CreatedAt,
                UpdatedAt = manifest.CreatedAt,
                Phase = RunPhase.Created,
                Conclusion = RunConclusion.Pending,
                Options = manifest.Options,
                Config = manifest.Config,
                Simulated = manifest.Simulation,
                Steps = manifest.Steps.Select(definition => new StepState
                {
                    StepId = definition.StepId,
                    Title = definition.Title,
                    Section = definition.Section,
                    SectionTitle = definition.SectionTitle,
                    Status = StepStatus.NotRun,
                    ManualRequirement = definition.ManualRequirement,
                    HardwareRelated = definition.HardwareRelated,
                    VersionAssertion = definition.VersionAssertion
                }).ToList()
            }
        };

        foreach (RunEvent item in ReadEvents(runId))
        {
            lock (session.Sync)
            {
                session.Events.Add(item);
                EventProjector.Apply(session, item);
            }
        }

        if (session.Snapshot.FinishedAt != null)
        {
            session.Snapshot.Executor.Alive = false;
        }

        session.Finished = session.Snapshot.Phase == RunPhase.Finished ||
                           session.Snapshot.Conclusion is RunConclusion.Passed
                               or RunConclusion.PassedWithSkips
                               or RunConclusion.Failed
                               or RunConclusion.Blocked
                               or RunConclusion.Interrupted;
        return session;
    }

    /// <summary>读取事件文件中的完整行；尾部半条记录被忽略。</summary>
    /// <param name="runId">运行标识。</param>
    /// <returns>事件序列。</returns>
    public IEnumerable<RunEvent> ReadEvents(string runId)
    {
        string path = _paths.EventsPath(runId);
        if (!File.Exists(path))
        {
            yield break;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "事件文件暂时不可读：{RunId}", runId);
            yield break;
        }

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            RunEvent? item = null;
            try
            {
                item = JsonSerializer.Deserialize<RunEvent>(line, JsonDefaults.File);
            }
            catch (JsonException)
            {
                // 执行器写入中途可能留下半条尾记录；只读取完整事件。
            }

            if (item != null)
            {
                yield return item;
            }
        }
    }

    /// <summary>应用执行器已落盘的新事件。</summary>
    /// <param name="session">本轮会话。</param>
    /// <param name="item">事件。</param>
    public void ApplyExternalEvent(RunSession session, RunEvent item)
    {
        if (item.Sequence <= session.LastSequence)
        {
            return;
        }

        lock (session.Sync)
        {
            session.Events.Add(item);
            EventProjector.Apply(session, item);
        }

        session.Hub.Publish(item);
    }

    /// <summary>追加宿主生成的合成事件（执行器已退出时使用），保证序号连续并落盘。</summary>
    /// <param name="session">本轮会话。</param>
    /// <param name="type">事件类型。</param>
    /// <param name="stepId">步骤编号。</param>
    /// <param name="phase">阶段。</param>
    /// <param name="status">步骤状态。</param>
    /// <param name="reason">原因。</param>
    /// <param name="message">说明。</param>
    /// <param name="data">附加数据。</param>
    /// <returns>已写入事件。</returns>
    public RunEvent AppendSyntheticEvent(
        RunSession session,
        EventType type,
        string? stepId = null,
        RunPhase? phase = null,
        StepStatus? status = null,
        string? reason = null,
        string? message = null,
        Dictionary<string, string>? data = null)
    {
        RunEvent item;
        lock (session.Sync)
        {
            item = new RunEvent
            {
                RunId = session.Snapshot.RunId,
                Sequence = session.Snapshot.LastSequence + 1,
                At = DateTimeOffset.Now,
                Type = type,
                StepId = stepId,
                Phase = phase,
                Status = status,
                Reason = reason,
                Message = message,
                Data = data
            };
            string line = JsonSerializer.Serialize(item, JsonDefaults.Line);
            File.AppendAllText(_paths.EventsPath(session.Snapshot.RunId), line + Environment.NewLine, new UTF8Encoding(false));
            session.Events.Add(item);
            EventProjector.Apply(session, item);
        }

        session.Hub.Publish(item);
        return item;
    }

    /// <summary>写出最终报告。</summary>
    /// <param name="session">本轮会话。</param>
    public void SaveReport(RunSession session)
    {
        RunReport report = BuildReport(session);
        string path = _paths.ReportPath(session.Snapshot.RunId);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(report, JsonDefaults.File), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporary, path);
    }

    /// <summary>构建报告。</summary>
    /// <param name="session">本轮会话。</param>
    /// <returns>报告。</returns>
    public RunReport BuildReport(RunSession session)
    {
        RunSnapshot snapshot = session.Snapshot;
        lock (session.Sync)
        {
            return new RunReport
            {
                RunId = snapshot.RunId,
                GeneratedAt = DateTimeOffset.Now,
                Conclusion = snapshot.Conclusion,
                Options = snapshot.Options,
                Config = snapshot.Config,
                Range = snapshot.Range,
                Steps = snapshot.Steps.Select(CloneStep).ToList(),
                Counts = snapshot.Counts,
                Cleanup = snapshot.Cleanup,
                PendingField = snapshot.PendingField,
                Artifacts = snapshot.Artifacts,
                Simulated = snapshot.Simulated,
                EventsCount = snapshot.LastSequence,
                Note = snapshot.Simulated
                    ? "模拟运行只用于契约与页面分支验证，不代表实机通过。"
                    : "本期覆盖 01—06 节 27 步；其余 157 步未实现。"
            };
        }
    }

    /// <summary>列出已有运行，最近创建的在前。</summary>
    /// <returns>运行标识。</returns>
    public IEnumerable<string> ListRunIds()
    {
        if (!Directory.Exists(_paths.RunsRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(_paths.RunsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && IsSafeRunId(name!))
            .Select(name => name!)
            .OrderByDescending(name => name, StringComparer.Ordinal);
    }

    /// <summary>运行标识只允许安全字符，防止目录穿越。</summary>
    /// <param name="runId">运行标识。</param>
    public static bool IsSafeRunId(string runId) =>
        !string.IsNullOrWhiteSpace(runId) &&
        runId.Length <= 64 &&
        runId.All(character => char.IsLetterOrDigit(character) || character == '-' || character == '_');

    /// <summary>复制步骤，避免报告与内存快照共享可变集合。</summary>
    private static StepState CloneStep(StepState step) => new()
    {
        StepId = step.StepId,
        Title = step.Title,
        Section = step.Section,
        SectionTitle = step.SectionTitle,
        Status = step.Status,
        Reason = step.Reason,
        StartedAt = step.StartedAt,
        FinishedAt = step.FinishedAt,
        DurationMs = step.DurationMs,
        Manual = step.Manual,
        ManualRequirement = step.ManualRequirement,
        HardwareRelated = step.HardwareRelated,
        HardwareSkipped = step.HardwareSkipped,
        VersionAssertion = step.VersionAssertion,
        EvidenceIds = new List<string>(step.EvidenceIds),
        Notes = new List<string>(step.Notes)
    };
}
