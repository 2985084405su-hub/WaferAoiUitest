namespace WaferAoi.UiTestTool.Contracts;

/// <summary>
/// 本机配置。只保存非敏感项；密码始终由测试机环境变量提供，不落盘、不回传。
/// </summary>
public sealed class ToolConfig
{
    /// <summary>被测程序 EXE 绝对路径。</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>配方目录。</summary>
    public string RecipeDir { get; set; } = @"D:\GlobalConfig\Recipe";

    /// <summary>证据根目录；本轮目录在其下按 runId 创建。</summary>
    public string EvidenceDir { get; set; } = string.Empty;

    /// <summary>预期版本号。</summary>
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>预期编译日期，格式 yyyy-MM-dd HH:mm 或 yyyy-MM-dd。</summary>
    public string BuildDate { get; set; } = string.Empty;

    /// <summary>模板配方名称，源工程固定为 1。</summary>
    public string TemplateRecipeName { get; set; } = "1";

    /// <summary>固定登录账号。</summary>
    public string UserName { get; set; } = "admin";

    /// <summary>生产配置目录。</summary>
    public string ConfigurationRoot { get; set; } = @"D:\GlobalConfig\Config";

    /// <summary>页面默认选项；执行时重新固定到 runId。</summary>
    public RunOptions Options { get; set; } = new();

    /// <summary>最近一次保存时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>配置读取响应；包含计算字段，不包含密码。</summary>
public sealed class ConfigView
{
    /// <summary>被测程序 EXE 绝对路径。</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>配方目录。</summary>
    public string RecipeDir { get; set; } = string.Empty;

    /// <summary>证据根目录。</summary>
    public string EvidenceDir { get; set; } = string.Empty;

    /// <summary>预期版本号。</summary>
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>预期编译日期。</summary>
    public string BuildDate { get; set; } = string.Empty;

    /// <summary>模板配方名称。</summary>
    public string TemplateRecipeName { get; set; } = "1";

    /// <summary>固定登录账号。</summary>
    public string UserName { get; set; } = "admin";

    /// <summary>生产配置目录。</summary>
    public string ConfigurationRoot { get; set; } = @"D:\GlobalConfig\Config";

    /// <summary>页面默认选项。</summary>
    public RunOptions Options { get; set; } = new();

    /// <summary>是否已通过环境变量配置密码。</summary>
    public bool PasswordConfigured { get; set; }

    /// <summary>密码来源环境变量名。</summary>
    public string PasswordEnvironmentVariable { get; set; } = "WAFER_AOI_UI_PASSWORD";

    /// <summary>本机数据目录。</summary>
    public string DataDir { get; set; } = string.Empty;

    /// <summary>配置更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>配置保存请求；字段为 null 表示不修改。</summary>
public sealed class SaveConfigRequest
{
    /// <summary>被测程序 EXE 绝对路径。</summary>
    public string? ExePath { get; set; }

    /// <summary>配方目录。</summary>
    public string? RecipeDir { get; set; }

    /// <summary>证据根目录。</summary>
    public string? EvidenceDir { get; set; }

    /// <summary>预期版本号。</summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>预期编译日期。</summary>
    public string? BuildDate { get; set; }

    /// <summary>模板配方名称。</summary>
    public string? TemplateRecipeName { get; set; }

    /// <summary>固定登录账号。</summary>
    public string? UserName { get; set; }

    /// <summary>生产配置目录。</summary>
    public string? ConfigurationRoot { get; set; }

    /// <summary>页面默认选项。</summary>
    public RunOptions? Options { get; set; }
}
