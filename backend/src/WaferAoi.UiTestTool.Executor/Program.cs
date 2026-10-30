using System.Text.Json;
using System.Text.Json.Serialization;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor;

/// <summary>
/// 执行器入口。Web 宿主为本轮写 manifest.json 并通过命令行传入绝对路径；
/// 执行器只信任 manifest 中的快照，不读取可能已变化的宿主环境配置。
/// </summary>
internal static class Program
{
    /// <summary>进程退出码：0 表示执行器正常收敛（业务结论可能是失败或阻塞）；非 0 表示执行器自身异常。</summary>
    private const int ExitOk = 0;

    /// <summary>命令行参数不完整或清单不可读。</summary>
    private const int ExitInvalidArguments = 64;

    /// <summary>执行器自身未处理异常。</summary>
    private const int ExitUnhandledError = 70;

    /// <summary>解析参数、执行主流程并返回退出码。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    private static int Main(string[] args)
    {
        ExecutorArguments arguments;
        try
        {
            arguments = ExecutorArguments.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitInvalidArguments;
        }

        try
        {
            RunManifest manifest = ManifestReader.Read(arguments.ManifestPath);
            if (arguments.Simulate)
            {
                manifest.Simulation = true;
                manifest.Scenario ??= arguments.Scenario;
            }

            using (var writer = new EventWriter(manifest.RunId, Path.Combine(manifest.RunDir, "events.jsonl")))
            using (var channel = new CommandChannel(
                       manifest.RunId,
                       Path.Combine(manifest.RunDir, "commands"),
                       Path.Combine(manifest.RunDir, "acks")))
            {
                if (manifest.Steps.Count != MainFlowCatalog.ImplementedStepCount)
                {
                    // 宿主与执行器不是同一次构建时，也必须发布终结事件；否则宿主只能按“未发布 run.finished”保持占用。
                    string mismatch =
                        $"运行清单步骤数为 {manifest.Steps.Count}，本期必须为 {MainFlowCatalog.ImplementedStepCount}；" +
                        "宿主与执行器版本不一致，本轮未启动被测程序。";
                    writer.Emit(EventType.Log, message: mismatch);
                    writer.Emit(EventType.RunFinished, phase: RunPhase.Finished, reason: mismatch);
                    Console.Error.WriteLine(mismatch);
                    return ExitInvalidArguments;
                }

                var checkpoints = new CheckpointGate(
                    manifest,
                    writer,
                    channel,
                    TimeSpan.FromSeconds(Math.Max(5, manifest.CheckpointTimeoutSeconds)));

                writer.Emit(
                    EventType.Log,
                    message: $"执行器已启动：pid={System.Diagnostics.Process.GetCurrentProcess().Id}，simulation={manifest.Simulation}。");

                IExecutorFlow flow = manifest.Simulation
                    ? new SimulationFlow(manifest, writer, checkpoints)
                    : new LiveFlow(manifest, writer, checkpoints);

                flow.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            return ExitOk;
        }
        catch (Exception exception)
        {
            // 退出码非 0 时 Web 会按“执行器异常退出”处理，并把最后事件保留为现场依据。
            Console.Error.WriteLine(exception.ToString());
            return ExitUnhandledError;
        }
    }
}

/// <summary>执行器命令行参数。</summary>
internal sealed class ExecutorArguments
{
    /// <summary>manifest.json 绝对路径。</summary>
    public string ManifestPath { get; private set; } = string.Empty;

    /// <summary>是否强制模拟，仅用于契约联调。</summary>
    public bool Simulate { get; private set; }

    /// <summary>模拟场景名。</summary>
    public string? Scenario { get; private set; }

    /// <summary>解析参数；只接受白名单参数，避免执行器变成通用命令入口。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>解析结果。</returns>
    public static ExecutorArguments Parse(string[] args)
    {
        var result = new ExecutorArguments();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--manifest":
                    result.ManifestPath = NextValue(args, ref index);
                    break;
                case "--simulate":
                    result.Simulate = true;
                    break;
                case "--scenario":
                    result.Scenario = NextValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(result.ManifestPath))
        {
            throw new ArgumentException("缺少 --manifest <manifest.json>。");
        }

        return result;
    }

    /// <summary>读取参数值并推进索引。</summary>
    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {args[index]} 缺少取值。");
        }

        return args[++index];
    }
}

/// <summary>读取并校验执行清单。</summary>
internal static class ManifestReader
{
    /// <summary>反序列化 manifest 并校验必需字段。</summary>
    /// <param name="path">manifest 绝对路径。</param>
    /// <returns>执行清单。</returns>
    public static RunManifest Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到运行清单。", path);
        }

        RunManifest? manifest = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(path), JsonDefaults.Executor);
        if (manifest == null ||
            string.IsNullOrWhiteSpace(manifest.RunId) ||
            string.IsNullOrWhiteSpace(manifest.RunDir))
        {
            throw new InvalidDataException("运行清单缺少 runId 或 runDir。");
        }


        Directory.CreateDirectory(manifest.RunDir);
        Directory.CreateDirectory(Path.Combine(manifest.RunDir, "artifacts"));
        return manifest;
    }
}

/// <summary>执行器共用 JSON 选项。</summary>
internal static class JsonDefaults
{
    /// <summary>文件与协议统一使用 camelCase 和字符串枚举。</summary>
    public static readonly JsonSerializerOptions Executor = Create();

    /// <summary>创建选项实例。</summary>
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
