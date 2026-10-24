using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Executor.Real;

/// <summary>
/// 真实执行配置：全部来自本轮 manifest 的快照，不从执行器进程环境重新读取配置，
/// 避免运行创建后宿主或环境变化改变已创建运行的输入。
/// 密码是唯一例外，只从环境变量读取，不落盘、不写日志、不回读。
/// </summary>
internal sealed class ExecutorSettings
{
    /// <summary>密码环境变量名，与 Web 配置一致。</summary>
    public const string PasswordVariable = "WAFER_AOI_UI_PASSWORD";

    /// <summary>被测程序 EXE。</summary>
    public string ApplicationPath { get; private init; } = string.Empty;

    /// <summary>生产配置目录。</summary>
    public string ConfigurationRoot { get; private init; } = string.Empty;

    /// <summary>配方目录。</summary>
    public string RecipeRoot { get; private init; } = string.Empty;

    /// <summary>模板配方名。</summary>
    public string TemplateRecipeName { get; private init; } = "1";

    /// <summary>本轮证据目录。</summary>
    public string ArtifactsRoot { get; private init; } = string.Empty;

    /// <summary>固定登录账号。</summary>
    public string UserName { get; private init; } = "admin";

    /// <summary>登录密码；只在内存中使用。</summary>
    public string Password { get; private init; } = string.Empty;

    /// <summary>启动等待上限。</summary>
    public TimeSpan StartupTimeout { get; private init; } = TimeSpan.FromMinutes(5);

    /// <summary>普通界面等待上限。</summary>
    public TimeSpan UiTimeout { get; private init; } = TimeSpan.FromSeconds(20);

    /// <summary>启动提示等待上限。</summary>
    public TimeSpan StartupPromptTimeout { get; private init; } = TimeSpan.FromMinutes(3);

    /// <summary>硬件动作（上片、回零、运行角度校正）等待上限。</summary>
    public TimeSpan HardwareActionTimeout { get; private init; } = TimeSpan.FromMinutes(10);

    /// <summary>预期版本号。</summary>
    public string ExpectedVersion { get; private init; } = string.Empty;

    /// <summary>预期编译日期。</summary>
    public string ExpectedBuildDate { get; private init; } = string.Empty;

    /// <summary>按清单构造配置。</summary>
    /// <param name="manifest">本轮清单。</param>
    /// <returns>不可变配置。</returns>
    public static ExecutorSettings FromManifest(RunManifest manifest)
    {
        RunConfigSnapshot config = manifest.Config;
        return new ExecutorSettings
        {
            ApplicationPath = config.ExePath,
            ConfigurationRoot = string.IsNullOrWhiteSpace(config.ConfigurationRoot)
                ? @"D:\GlobalConfig\Config"
                : config.ConfigurationRoot,
            RecipeRoot = config.RecipeDir,
            TemplateRecipeName = string.IsNullOrWhiteSpace(config.TemplateRecipeName)
                ? "1"
                : config.TemplateRecipeName,
            ArtifactsRoot = Path.Combine(manifest.RunDir, "artifacts"),
            UserName = string.IsNullOrWhiteSpace(config.UserName) ? "admin" : config.UserName,
            Password = (Environment.GetEnvironmentVariable(PasswordVariable) ?? string.Empty).Trim(),
            ExpectedVersion = config.ExpectedVersion ?? string.Empty,
            ExpectedBuildDate = config.BuildDate ?? string.Empty
        };
    }

    /// <summary>启动前检查：路径、目录、提权与密码是否齐备。</summary>
    /// <returns>检查问题列表；为空表示可以启动。</returns>
    public IReadOnlyList<string> CheckPrerequisites()
    {
        var problems = new List<string>();
        if (!IsAdministrator())
        {
            problems.Add("执行器未以管理员身份运行；被测程序与 UIA 操作需要提权。");
        }

        if (string.IsNullOrWhiteSpace(ApplicationPath) || !File.Exists(ApplicationPath))
        {
            problems.Add("被测程序不存在：" + (string.IsNullOrWhiteSpace(ApplicationPath) ? "(未配置)" : ApplicationPath));
        }

        if (!Directory.Exists(ConfigurationRoot))
        {
            problems.Add("生产配置目录不存在：" + ConfigurationRoot);
        }

        if (!Directory.Exists(RecipeRoot))
        {
            problems.Add("配方目录不存在：" + RecipeRoot);
        }

        if (string.IsNullOrEmpty(Password))
        {
            problems.Add($"缺少现场密码：请设置环境变量 {PasswordVariable}（只从环境读取，不落盘）。");
        }

        return problems;
    }

    /// <summary>当前进程是否管理员。</summary>
    /// <returns>管理员为 true。</returns>
    private static bool IsAdministrator()
    {
        using System.Security.Principal.WindowsIdentity identity =
            System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
