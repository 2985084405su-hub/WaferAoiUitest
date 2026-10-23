namespace WaferAoi.UiTestTool.Web;

/// <summary>本机宿主启动参数。</summary>
internal sealed class WebArguments
{
    /// <summary>默认端口；只绑定 127.0.0.1。</summary>
    public const int DefaultPort = 5180;

    /// <summary>监听端口。</summary>
    public int Port { get; private set; } = DefaultPort;

    /// <summary>本机数据目录。</summary>
    public string? DataDir { get; private set; }

    /// <summary>前端构建产物目录。</summary>
    public string? WebRoot { get; private set; }

    /// <summary>执行器 exe 路径。</summary>
    public string? ExecutorPath { get; private set; }

    /// <summary>强制模拟执行器；仅用于契约联调。</summary>
    public bool Simulate { get; private set; }

    /// <summary>模拟场景。</summary>
    public string? Scenario { get; private set; }

    /// <summary>解析参数与受支持的环境变量。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>解析结果。</returns>
    public static WebArguments Parse(string[] args)
    {
        var result = new WebArguments();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--port":
                    result.Port = int.Parse(NextValue(args, ref index));
                    break;
                case "--data-dir":
                    result.DataDir = NextValue(args, ref index);
                    break;
                case "--web-root":
                    result.WebRoot = NextValue(args, ref index);
                    break;
                case "--executor":
                    result.ExecutorPath = NextValue(args, ref index);
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

        if (result.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "端口必须在 1—65535 之间。");
        }

        result.DataDir ??= Environment.GetEnvironmentVariable("WAFER_UI_TESTTOOL_DATA_DIR");
        result.WebRoot ??= Environment.GetEnvironmentVariable("WAFER_UI_TESTTOOL_WEB_ROOT");
        result.ExecutorPath ??= Environment.GetEnvironmentVariable("WAFER_UI_TESTTOOL_EXECUTOR");
        return result;
    }

    /// <summary>读取参数值。</summary>
    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {args[index]} 缺少取值。");
        }

        return args[++index];
    }
}
