using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;
using WaferAoi.UiTestTool.Web.Runs;
using WaferAoi.UiTestTool.Web.Storage;

namespace WaferAoi.UiTestTool.Web;

/// <summary>
/// HTTP 接口。全部只在本机回环地址提供；页面通过快照 + SSE 事件展示状态，
/// 不从日志推断步骤结果，也不提供任意进程或磁盘访问。
/// </summary>
internal static class ApiEndpoints
{
    /// <summary>注册全部 API。</summary>
    /// <param name="app">Web 应用。</param>
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            protocolVersion = 1,
            at = DateTimeOffset.Now
        }));

        app.MapGet("/api/catalog/steps", () => Results.Json(new
        {
            implemented = MainFlowCatalog.ImplementedStepCount,
            designed = MainFlowCatalog.DesignedStepCount,
            unimplemented = MainFlowCatalog.UnimplementedStepCount,
            steps = MainFlowCatalog.Steps
        }, JsonDefaults.Api));

        app.MapGet("/api/config", (ConfigStore store) => Results.Json(store.GetView(), JsonDefaults.Api));
        app.MapPut("/api/config", (SaveConfigRequest request, ConfigStore store) =>
            Results.Json(store.Save(request), JsonDefaults.Api));
        app.MapPost("/api/config/validate", (ValidateConfigRequest? request, RunManager manager) =>
            Results.Json(manager.Validate(request?.SkipVersionAssertion), JsonDefaults.Api));

        app.MapGet("/api/runs/current", (RunManager manager) =>
        {
            RunSnapshot? snapshot = manager.GetCurrent();
            return snapshot == null
                ? Results.Json((RunSnapshot?)null, JsonDefaults.Api)
                : Results.Json(snapshot, JsonDefaults.Api);
        });

        app.MapPost("/api/runs", async (CreateRunRequest request, RunManager manager) =>
        {
            CreateRunResponse response = await manager.CreateRunAsync(request).ConfigureAwait(false);
            return Results.Json(response, JsonDefaults.Api, statusCode: response.Created ? 201 : 200);
        });

        app.MapGet("/api/runs", (int? limit, int? offset, RunManager manager) =>
        {
            int take = Math.Clamp(limit ?? 50, 1, 100);
            int skip = Math.Max(0, offset ?? 0);
            IReadOnlyList<RunSummary> items = manager.ListRuns(take, skip);
            return Results.Json(new
            {
                items,
                total = items.Count,
                limit = take,
                offset = skip
            }, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}", (string runId, RunManager manager) =>
        {
            RunSnapshot? snapshot = manager.GetRun(runId);
            return snapshot == null
                ? throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。")
                : Results.Json(snapshot, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}/steps", (string runId, RunManager manager) =>
        {
            RunSnapshot? snapshot = manager.GetRun(runId);
            return snapshot == null
                ? throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。")
                : Results.Json(new
                {
                    runId = snapshot.RunId,
                    range = snapshot.Range,
                    counts = snapshot.Counts,
                    steps = snapshot.Steps
                }, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}/report", (string runId, RunManager manager) =>
        {
            RunReport? report = manager.GetReport(runId);
            return report == null
                ? throw new ApiException(StatusCodes.Status404NotFound, "report_not_found", "找不到本轮报告。")
                : Results.Json(report, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}/artifacts", (string runId, RunManager manager) =>
        {
            RunSnapshot? snapshot = manager.GetRun(runId);
            if (snapshot == null)
            {
                throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。");
            }

            return Results.Json(new
            {
                items = snapshot.Artifacts.Select(item => new
                {
                    item.ArtifactId,
                    item.FileName,
                    item.Kind,
                    item.MimeType,
                    item.Size,
                    item.CreatedAt,
                    item.StepId,
                    Available = File.Exists(Path.Combine(
                        manager.GetSession(runId)!.RunDirectory,
                        "artifacts",
                        item.FileName))
                })
            }, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}/artifacts/{artifactId}", (string runId, string artifactId, RunManager manager) =>
        {
            RunSession? session = manager.GetSession(runId);
            if (session == null)
            {
                throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。");
            }

            ArtifactInfo? artifact = session.Snapshot.Artifacts.FirstOrDefault(item =>
                string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal));
            if (artifact == null)
            {
                throw new ApiException(StatusCodes.Status404NotFound, "artifact_not_found", "该证据未在本轮登记。");
            }

            string directory = Path.GetFullPath(Path.Combine(session.RunDirectory, "artifacts"));
            string path = Path.GetFullPath(Path.Combine(directory, artifact.FileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                throw new ApiException(StatusCodes.Status404NotFound, "artifact_missing", "证据文件不存在或已被移走。");
            }

            return Results.File(path, MimeFor(path), enableRangeProcessing: true);
        });

        app.MapPost(
            "/api/runs/{runId}/checkpoints/{checkpointId}/confirm",
            (string runId, string checkpointId, ConfirmCheckpointRequest request, RunManager manager) =>
            {
                ConfirmCheckpointResponse response = manager.ConfirmCheckpoint(runId, checkpointId, request);
                return Results.Json(response, JsonDefaults.Api, statusCode: StatusCodes.Status202Accepted);
            });

        app.MapPost("/api/runs/{runId}/field-resolution", (string runId, FieldResolutionRequest request, RunManager manager) =>
        {
            FieldResolutionResponse response = manager.ResolvePendingField(runId, request);
            return Results.Json(response, JsonDefaults.Api);
        });

        app.MapGet("/api/runs/{runId}/events", StreamEventsAsync);
    }

    /// <summary>SSE 事件流：先补发 after 之后的历史事件，再推送增量，并每 15 秒发送心跳注释。</summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="runId">运行标识。</param>
    /// <param name="after">客户端最后收到的序号。</param>
    /// <param name="manager">运行编排器。</param>
    private static async Task StreamEventsAsync(
        HttpContext context,
        string runId,
        long? after,
        RunManager manager)
    {
        RunSession? session = manager.GetSession(runId);
        if (session == null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "run_not_found", "找不到本轮运行。");
        }

        long lastSent = after ?? 0;
        if (lastSent <= 0 &&
            long.TryParse(context.Request.Headers["Last-Event-ID"].ToString(), out long lastEventId))
        {
            lastSent = lastEventId;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.Headers["Connection"] = "keep-alive";

        // 先订阅再补发，避免补发与实时事件之间出现缺口；重复序号由 lastSent 过滤。
        var reader = session.Hub.Subscribe();

        List<RunEvent> replay;
        lock (session.Sync)
        {
            replay = session.Events.Where(item => item.Sequence > lastSent).ToList();
        }

        foreach (RunEvent item in replay)
        {
            await WriteEventAsync(context, item).ConfigureAwait(false);
            lastSent = item.Sequence;
        }

        int idleTicks = 0;
        while (!context.RequestAborted.IsCancellationRequested)
        {
            bool wrote = false;
            while (reader.TryRead(out RunEvent? item))
            {
                if (item.Sequence <= lastSent)
                {
                    continue;
                }

                await WriteEventAsync(context, item).ConfigureAwait(false);
                lastSent = item.Sequence;
                wrote = true;
            }

            await Task.Delay(300, context.RequestAborted).ConfigureAwait(false);
            idleTicks = wrote ? 0 : idleTicks + 1;
            if (idleTicks > 0 && idleTicks % 50 == 0)
            {
                await context.Response.WriteAsync(": ping\n\n", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    /// <summary>写出单条 SSE 事件。</summary>
    private static async Task WriteEventAsync(HttpContext context, RunEvent item)
    {
        string json = JsonSerializer.Serialize(item, JsonDefaults.Api);
        string payload = $"id: {item.Sequence}\nevent: {WireName(item.Type)}\ndata: {json}\n\n";
        await context.Response.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>事件在 SSE 中的名称。</summary>
    private static string WireName(EventType type) => type switch
    {
        EventType.RunCreated => "runCreated",
        EventType.RunPreparing => "runPreparing",
        EventType.RunPrepared => "runPrepared",
        EventType.RunStarted => "runStarted",
        EventType.RunPhase => "runPhase",
        EventType.StepStarted => "stepStarted",
        EventType.StepWaiting => "stepWaiting",
        EventType.StepFinished => "stepFinished",
        EventType.StepUnknown => "stepUnknown",
        EventType.CheckpointCreated => "checkpointCreated",
        EventType.CheckpointResolved => "checkpointResolved",
        EventType.Log => "log",
        EventType.ArtifactCreated => "artifactCreated",
        EventType.CleanupStarted => "cleanupStarted",
        EventType.CleanupFinished => "cleanupFinished",
        EventType.PendingFieldEntered => "pendingFieldEntered",
        EventType.PendingFieldResolved => "pendingFieldResolved",
        EventType.LockReleased => "lockReleased",
        EventType.RunFinished => "runFinished",
        _ => "unknown"
    };

    /// <summary>证据 MIME 白名单；HTML 一律按下载处理，避免在本机同源页面执行脚本。</summary>
    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".json" => "application/json; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".log" => "text/plain; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".trx" => "application/xml; charset=utf-8",
        _ => "application/octet-stream"
    };
}

/// <summary>配置校验请求。</summary>
internal sealed class ValidateConfigRequest
{
    /// <summary>按“跳过版本断言”口径校验；为空时使用配置默认选项。</summary>
    public bool? SkipVersionAssertion { get; set; }
}
