using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Web.Runs;

/// <summary>
/// 把有序事件投影为运行快照。页面、报告和历史都使用同一投影结果，
/// 不解析中文日志，也不允许前端二次推导步骤状态。
/// </summary>
internal static class EventProjector
{
    /// <summary>应用一条事件；旧序号或重复事件直接忽略。</summary>
    /// <param name="session">本轮会话。</param>
    /// <param name="item">事件。</param>
    public static void Apply(RunSession session, RunEvent item)
    {
        lock (session.Sync)
        {
            if (item.Sequence <= session.Snapshot.LastSequence)
            {
                return;
            }

            ApplyLocked(session, item);
        }
    }

    /// <summary>应用事件并刷新计数。</summary>
    private static void ApplyLocked(RunSession session, RunEvent item)
    {
        RunSnapshot snapshot = session.Snapshot;
        snapshot.LastSequence = item.Sequence;
        snapshot.UpdatedAt = item.At;
        // Alive 只由执行器启动/退出的生命周期决定；投影（含宿主补写的终结事件）只更新
        // 最近事件时间与序号，否则中断后补写的事件会把 FinalizeAsync 设的 false 覆盖回 true。
        snapshot.Executor.LastEventAt = item.At;
        snapshot.Executor.LastSequence = item.Sequence;

        switch (item.Type)
        {
            case EventType.RunCreated:
                if (item.Phase.HasValue) snapshot.Phase = item.Phase.Value;
                break;

            case EventType.RunPreparing:
                snapshot.Phase = RunPhase.Preparing;
                break;

            case EventType.RunPrepared:
                snapshot.Phase = RunPhase.Preparing;
                snapshot.LastMessage = item.Message ?? item.Reason ?? string.Empty;
                break;

            case EventType.RunStarted:
                snapshot.Phase = RunPhase.Executing;
                break;

            case EventType.RunPhase:
                if (item.Phase.HasValue) snapshot.Phase = item.Phase.Value;
                break;

            case EventType.StepStarted:
                ApplyStepStarted(session, item);
                break;

            case EventType.StepWaiting:
                ApplyStepStatus(session, item, StepStatus.Waiting, item.Reason);
                break;

            case EventType.CheckpointCreated:
                ApplyCheckpointCreated(session, item);
                break;

            case EventType.CheckpointResolved:
                ApplyCheckpointResolved(session, item);
                break;

            case EventType.StepFinished:
                ApplyStepFinished(session, item);
                break;

            case EventType.StepUnknown:
                ApplyStepStatus(session, item, StepStatus.Unknown, item.Reason);
                break;

            case EventType.ArtifactCreated:
                ApplyArtifact(session, item);
                break;

            case EventType.CleanupStarted:
                snapshot.Cleanup.Status = CleanupStatus.Running;
                snapshot.Cleanup.StartedAt = item.At;
                snapshot.Phase = RunPhase.Cleaning;
                break;

            case EventType.CleanupFinished:
                ApplyCleanupFinished(session, item);
                break;

            case EventType.PendingFieldEntered:
                snapshot.PendingField.Active = true;
                snapshot.PendingField.Since ??= item.At;
                snapshot.PendingField.Reason = item.Reason ?? item.Message ?? "硬件状态未确认。";
                if (item.Data != null && item.Data.TryGetValue("processId", out string? processId) &&
                    int.TryParse(processId, out int parsedProcessId))
                {
                    snapshot.PendingField.ProcessId = parsedProcessId;
                }

                snapshot.Phase = RunPhase.PendingField;
                snapshot.Conclusion = RunConclusion.PendingField;
                break;

            case EventType.PendingFieldResolved:
                snapshot.PendingField.Active = false;
                snapshot.PendingField.Note = item.Message ?? item.Data?.GetValueOrDefault("note") ?? string.Empty;
                snapshot.PendingField.ProcessExitedVerified = item.Data?.GetValueOrDefault("processExitedVerified") == "true";
                snapshot.PendingField.DeviceStateVerified = item.Data?.GetValueOrDefault("deviceStateVerified") == "true";
                break;

            case EventType.LockReleased:
                snapshot.PendingField.Active = false;
                break;

            case EventType.Log:
                snapshot.LastMessage = item.Message ?? item.Reason ?? snapshot.LastMessage;
                break;

            case EventType.RunFinished:
                snapshot.FinishedAt = item.At;
                if (item.Phase.HasValue)
                {
                    snapshot.Phase = item.Phase.Value;
                }

                if (!string.IsNullOrWhiteSpace(item.Reason))
                {
                    snapshot.LastMessage = item.Reason!;
                }

                if (snapshot.Cleanup.Status == CleanupStatus.NotStarted)
                {
                    snapshot.Cleanup.Status = CleanupStatus.NotRequired;
                }

                snapshot.Conclusion = DetermineConclusion(snapshot);
                break;
        }

        if (item.Status == StepStatus.Running)
        {
            foreach (StepState running in snapshot.Steps.Where(step => step.Status == StepStatus.Running))
            {
                running.DurationMs = running.StartedAt.HasValue
                    ? (long)Math.Max(0, (item.At - running.StartedAt.Value).TotalMilliseconds)
                    : null;
            }
        }

        Recalculate(snapshot);
    }

    /// <summary>步骤开始。</summary>
    private static void ApplyStepStarted(RunSession session, RunEvent item)
    {
        StepState? step = FindStep(session, item.StepId);
        if (step == null)
        {
            return;
        }

        step.Status = StepStatus.Running;
        step.StartedAt = item.At;
        step.FinishedAt = null;
        step.DurationMs = 0;
        step.Reason = string.Empty;
        session.Snapshot.Phase = RunPhase.Executing;
    }

    /// <summary>更新步骤状态和原因。</summary>
    private static void ApplyStepStatus(RunSession session, RunEvent item, StepStatus status, string? reason)
    {
        StepState? step = FindStep(session, item.StepId);
        if (step == null)
        {
            return;
        }

        step.Status = status;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            step.Reason = reason!;
        }

        if (status == StepStatus.Waiting)
        {
            session.Snapshot.Phase = RunPhase.WaitingHuman;
        }
    }

    /// <summary>步骤终态。</summary>
    private static void ApplyStepFinished(RunSession session, RunEvent item)
    {
        StepState? step = FindStep(session, item.StepId);
        if (step == null || !item.Status.HasValue)
        {
            return;
        }

        step.Status = item.Status.Value;
        step.FinishedAt = item.At;
        step.DurationMs = step.StartedAt.HasValue
            ? (long)Math.Max(0, (item.At - step.StartedAt.Value).TotalMilliseconds)
            : 0;
        step.Reason = item.Reason ?? string.Empty;
        step.Manual = item.Status.Value == StepStatus.OperatorConfirmed;
        step.HardwareSkipped = item.Status.Value is StepStatus.HardwareSkipped or StepStatus.PassedWithHardwareSkipped;

        if (session.Snapshot.Phase == RunPhase.WaitingHuman)
        {
            session.Snapshot.Phase = RunPhase.Executing;
        }
    }

    /// <summary>确认点创建。</summary>
    private static void ApplyCheckpointCreated(RunSession session, RunEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.CheckpointId))
        {
            return;
        }

        DateTimeOffset createdAt = item.At;
        DateTimeOffset expiresAt = item.At.AddMinutes(30);
        string instruction = item.Message ?? string.Empty;
        if (item.Data != null)
        {
            if (item.Data.TryGetValue("startedAt", out string? startedAt) &&
                DateTimeOffset.TryParse(startedAt, out DateTimeOffset parsedStart))
            {
                createdAt = parsedStart;
            }

            if (item.Data.TryGetValue("expiresAt", out string? expiresAtText) &&
                DateTimeOffset.TryParse(expiresAtText, out DateTimeOffset parsedExpiry))
            {
                expiresAt = parsedExpiry;
            }

            if (item.Data.TryGetValue("instruction", out string? instructionText))
            {
                instruction = instructionText;
            }
        }

        var checkpoint = new CheckpointState
        {
            CheckpointId = item.CheckpointId!,
            StepId = item.StepId ?? string.Empty,
            Instruction = instruction,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            Status = CheckpointStatus.Waiting
        };
        session.Snapshot.Checkpoint = checkpoint;
        session.Snapshot.Phase = RunPhase.WaitingHuman;

        StepState? step = FindStep(session, item.StepId);
        if (step != null)
        {
            step.Status = StepStatus.Waiting;
            step.Reason = instruction;
        }
    }

    /// <summary>确认点已处理或超时。</summary>
    private static void ApplyCheckpointResolved(RunSession session, RunEvent item)
    {
        CheckpointState? checkpoint = session.Snapshot.Checkpoint;
        if (checkpoint == null ||
            !string.Equals(checkpoint.CheckpointId, item.CheckpointId, StringComparison.Ordinal))
        {
            return;
        }

        string result = item.Data?.GetValueOrDefault("result") ?? string.Empty;
        checkpoint.Status = string.Equals(result, "timeout", StringComparison.OrdinalIgnoreCase)
            ? CheckpointStatus.Expired
            : CheckpointStatus.Resolved;
        checkpoint.Note = item.Data?.GetValueOrDefault("note") ?? item.Message ?? string.Empty;
        checkpoint.ResolvedAt = item.At;
        checkpoint.RequestId = item.Data?.GetValueOrDefault("requestId") ?? string.Empty;
        if (Enum.TryParse(result, true, out CheckpointResult parsed))
        {
            checkpoint.Result = parsed;
        }
    }

    /// <summary>证据登记。</summary>
    private static void ApplyArtifact(RunSession session, RunEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.ArtifactId) || string.IsNullOrWhiteSpace(item.ArtifactName))
        {
            return;
        }

        string safeName = Path.GetFileName(item.ArtifactName);
        string path = Path.Combine(session.RunDirectory, "artifacts", safeName);
        var artifact = new ArtifactInfo
        {
            ArtifactId = item.ArtifactId!,
            FileName = safeName,
            Kind = item.ArtifactKind ?? "other",
            MimeType = item.MimeType ?? "application/octet-stream",
            Size = item.ArtifactSize ?? 0,
            CreatedAt = item.At,
            StepId = item.StepId ?? string.Empty,
            Available = File.Exists(path)
        };
        session.Snapshot.Artifacts.Add(artifact);

        StepState? step = FindStep(session, item.StepId);
        if (step != null && !step.EvidenceIds.Contains(artifact.ArtifactId))
        {
            step.EvidenceIds.Add(artifact.ArtifactId);
        }
    }

    /// <summary>清理结束。</summary>
    private static void ApplyCleanupFinished(RunSession session, RunEvent item)
    {
        string status = item.Data?.GetValueOrDefault("status") ?? "succeeded";
        session.Snapshot.Cleanup.Status = status.ToLowerInvariant() switch
        {
            "failed" => CleanupStatus.Failed,
            "notrequired" => CleanupStatus.NotRequired,
            "running" => CleanupStatus.Running,
            _ => CleanupStatus.Succeeded
        };
        session.Snapshot.Cleanup.FinishedAt = item.At;
        session.Snapshot.Cleanup.Reason = item.Reason ?? item.Message ?? string.Empty;
        session.Snapshot.Phase = RunPhase.Cleaning;
    }

    /// <summary>计算结论。</summary>
    private static RunConclusion DetermineConclusion(RunSnapshot snapshot)
    {
        if (snapshot.PendingField.Active)
        {
            return RunConclusion.PendingField;
        }

        if (snapshot.Steps.Any(step => step.Status == StepStatus.Failed))
        {
            return RunConclusion.Failed;
        }

        if (snapshot.Steps.Any(step => step.Status == StepStatus.Blocked))
        {
            return RunConclusion.Blocked;
        }

        if (snapshot.Steps.Any(step => step.Status == StepStatus.Unknown))
        {
            return RunConclusion.Interrupted;
        }

        if (snapshot.Steps.Any(step => step.Status is StepStatus.Running or StepStatus.Waiting))
        {
            return RunConclusion.Pending;
        }

        if (snapshot.Cleanup.Status == CleanupStatus.Failed)
        {
            return RunConclusion.Blocked;
        }

        if (snapshot.Steps.Any(step => step.Status == StepStatus.NotRun))
        {
            // 有步骤未开始却已结束：只能是提前停止或能力缺口，不能显示为通过。
            return RunConclusion.Blocked;
        }

        if (snapshot.Steps.Any(step => step.Status is StepStatus.HardwareSkipped or StepStatus.PassedWithHardwareSkipped or StepStatus.UserSkipped))
        {
            return RunConclusion.PassedWithSkips;
        }

        return RunConclusion.Passed;
    }

    /// <summary>重算计数。</summary>
    private static void Recalculate(RunSnapshot snapshot)
    {
        StepCounts counts = new() { Total = snapshot.Steps.Count };
        foreach (StepState step in snapshot.Steps)
        {
            switch (step.Status)
            {
                case StepStatus.Passed:
                    counts.Passed++;
                    break;
                case StepStatus.OperatorConfirmed:
                    counts.Passed++;
                    counts.ManualPassed++;
                    break;
                case StepStatus.PassedWithHardwareSkipped:
                case StepStatus.HardwareSkipped:
                case StepStatus.UserSkipped:
                    counts.Skipped++;
                    break;
                case StepStatus.Blocked:
                    counts.Blocked++;
                    break;
                case StepStatus.Failed:
                    counts.Failed++;
                    break;
                case StepStatus.Waiting:
                    counts.Waiting++;
                    break;
                case StepStatus.Unknown:
                    counts.Unknown++;
                    break;
                default:
                    counts.NotRun++;
                    break;
            }
        }

        snapshot.Counts = counts;
        if (snapshot.FinishedAt == null)
        {
            snapshot.Conclusion = snapshot.PendingField.Active ? RunConclusion.PendingField : RunConclusion.Pending;
        }
    }

    /// <summary>按编号查找步骤。</summary>
    private static StepState? FindStep(RunSession session, string? stepId) =>
        string.IsNullOrWhiteSpace(stepId)
            ? null
            : session.Snapshot.Steps.FirstOrDefault(step =>
                string.Equals(step.StepId, stepId, StringComparison.Ordinal));
}
