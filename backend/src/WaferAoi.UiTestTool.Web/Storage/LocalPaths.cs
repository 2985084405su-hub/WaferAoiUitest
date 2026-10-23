namespace WaferAoi.UiTestTool.Web.Storage;

/// <summary>
/// 本机目录约定。所有路径显式确定，不向上查找生产 sln，也不依赖原 UiTests 工程布局。
/// </summary>
internal sealed class LocalPaths
{
    /// <summary>本机数据根目录。</summary>
    public string DataRoot { get; private init; } = string.Empty;

    /// <summary>配置文件。</summary>
    public string ConfigPath { get; private init; } = string.Empty;

    /// <summary>机器占用与待现场处理状态。</summary>
    public string LockPath { get; private init; } = string.Empty;

    /// <summary>运行记录根目录。</summary>
    public string RunsRoot { get; private init; } = string.Empty;

    /// <summary>前端构建产物目录。</summary>
    public string WebRoot { get; private init; } = string.Empty;

    /// <summary>执行器 exe 路径。</summary>
    public string ExecutorPath { get; private init; } = string.Empty;

    /// <summary>创建目录约定。</summary>
    /// <param name="arguments">宿主参数。</param>
    /// <returns>路径集合。</returns>
    public static LocalPaths Create(WebArguments arguments)
    {
        string dataRoot = Path.GetFullPath(
            arguments.DataDir ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WaferAoiUiTestTool"));
        string webRoot = Path.GetFullPath(arguments.WebRoot ?? FindFrontendDist());
        string executorPath = Path.GetFullPath(arguments.ExecutorPath ?? FindExecutor());
        var paths = new LocalPaths
        {
            DataRoot = dataRoot,
            ConfigPath = Path.Combine(dataRoot, "config.json"),
            LockPath = Path.Combine(dataRoot, "lock.json"),
            RunsRoot = Path.Combine(dataRoot, "runs"),
            WebRoot = webRoot,
            ExecutorPath = executorPath
        };
        Directory.CreateDirectory(paths.DataRoot);
        Directory.CreateDirectory(paths.RunsRoot);
        return paths;
    }

    /// <summary>本轮目录。</summary>
    /// <param name="runId">运行标识。</param>
    public string RunDirectory(string runId) => Path.Combine(RunsRoot, runId);

    /// <summary>本轮事件文件。</summary>
    /// <param name="runId">运行标识。</param>
    public string EventsPath(string runId) => Path.Combine(RunDirectory(runId), "events.jsonl");

    /// <summary>本轮清单文件。</summary>
    /// <param name="runId">运行标识。</param>
    public string ManifestPath(string runId) => Path.Combine(RunDirectory(runId), "manifest.json");

    /// <summary>本轮证据目录。</summary>
    /// <param name="runId">运行标识。</param>
    public string ArtifactsDirectory(string runId) => Path.Combine(RunDirectory(runId), "artifacts");

    /// <summary>本轮报告文件。</summary>
    /// <param name="runId">运行标识。</param>
    public string ReportPath(string runId) => Path.Combine(RunDirectory(runId), "report.json");

    /// <summary>从宿主目录向上查找前端构建产物，仅用于开发期；发布包通过 --web-root 显式指定。</summary>
    private static string FindFrontendDist()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "frontend", "dist");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "frontend");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "web");
    }

    /// <summary>从宿主目录查找执行器；发布包通常把两个入口放在同一目录，执行器在子目录。</summary>
    private static string FindExecutor()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "WaferAoi.UiTestTool.Executor.exe");
        if (File.Exists(local))
        {
            return local;
        }

        string nested = Path.Combine(AppContext.BaseDirectory, "executor", "WaferAoi.UiTestTool.Executor.exe");
        if (File.Exists(nested))
        {
            return nested;
        }

        return local;
    }
}
