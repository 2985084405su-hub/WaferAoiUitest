using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Web.Storage;

/// <summary>
/// 本机配置读写。只保存非敏感配置；密码始终由测试机环境变量提供，
/// 读取与保存都不会回传或落盘密码值。
/// </summary>
internal sealed class ConfigStore
{
    /// <summary>密码环境变量名。</summary>
    public const string PasswordVariable = "WAFER_AOI_UI_PASSWORD";

    /// <summary>同步锁。</summary>
    private readonly object _sync = new();

    /// <summary>路径约定。</summary>
    private readonly LocalPaths _paths;

    /// <summary>日志。</summary>
    private readonly ILogger<ConfigStore> _logger;

    /// <summary>当前配置。</summary>
    private ToolConfig _config;

    /// <summary>创建存储并读取已有配置。</summary>
    /// <param name="paths">路径约定。</param>
    /// <param name="logger">日志。</param>
    public ConfigStore(LocalPaths paths, ILogger<ConfigStore> logger)
    {
        _paths = paths;
        _logger = logger;
        _config = Load();
    }

    /// <summary>取得当前配置的副本，避免调用方修改内存对象。</summary>
    /// <returns>配置副本。</returns>
    public ToolConfig Get()
    {
        lock (_sync)
        {
            return Clone(_config);
        }
    }

    /// <summary>取得配置视图。</summary>
    /// <returns>不含密码的配置视图。</returns>
    public ConfigView GetView()
    {
        lock (_sync)
        {
            ToolConfig config = _config;
            return new ConfigView
            {
                ExePath = config.ExePath,
                RecipeDir = config.RecipeDir,
                EvidenceDir = config.EvidenceDir,
                ExpectedVersion = config.ExpectedVersion,
                BuildDate = config.BuildDate,
                TemplateRecipeName = config.TemplateRecipeName,
                UserName = config.UserName,
                ConfigurationRoot = config.ConfigurationRoot,
                Options = new RunOptions
                {
                    SkipVersionAssertion = config.Options.SkipVersionAssertion,
                    HardwarePolicy = config.Options.HardwarePolicy
                },
                PasswordConfigured = PasswordConfigured(),
                PasswordEnvironmentVariable = PasswordVariable,
                DataDir = _paths.DataRoot,
                UpdatedAt = config.UpdatedAt
            };
        }
    }

    /// <summary>合并保存配置。</summary>
    /// <param name="request">保存请求；null 字段不修改。</param>
    /// <returns>保存后的视图。</returns>
    public ConfigView Save(SaveConfigRequest request)
    {
        lock (_sync)
        {
            ToolConfig next = Clone(_config);
            if (request.ExePath != null) next.ExePath = NormalizePath(request.ExePath);
            if (request.RecipeDir != null) next.RecipeDir = NormalizePath(request.RecipeDir);
            if (request.EvidenceDir != null) next.EvidenceDir = NormalizePath(request.EvidenceDir);
            if (request.ExpectedVersion != null) next.ExpectedVersion = request.ExpectedVersion.Trim();
            if (request.BuildDate != null) next.BuildDate = request.BuildDate.Trim();
            if (request.TemplateRecipeName != null) next.TemplateRecipeName = request.TemplateRecipeName.Trim();
            if (request.UserName != null) next.UserName = request.UserName.Trim();
            if (request.ConfigurationRoot != null) next.ConfigurationRoot = NormalizePath(request.ConfigurationRoot);
            if (request.Options != null)
            {
                if (!Enum.IsDefined(typeof(HardwarePolicy), request.Options.HardwarePolicy))
                {
                    throw new ApiException(StatusCodes.Status400BadRequest, "invalid_options", "硬件策略取值无效。");
                }

                next.Options = new RunOptions
                {
                    SkipVersionAssertion = request.Options.SkipVersionAssertion,
                    HardwarePolicy = request.Options.HardwarePolicy
                };
            }

            if (string.IsNullOrWhiteSpace(next.EvidenceDir))
            {
                next.EvidenceDir = Path.Combine(_paths.DataRoot, "evidence");
            }

            next.UpdatedAt = DateTimeOffset.Now;
            Write(next);
            _config = next;
            return GetView();
        }
    }

    /// <summary>判断密码是否已在环境变量中配置，只返回存在性。</summary>
    /// <returns>已配置时为 true。</returns>
    public static bool PasswordConfigured() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordVariable));

    /// <summary>读取已有配置；文件损坏时保留原文件并按默认值启动，避免宿主无法启动。</summary>
    private ToolConfig Load()
    {
        if (!File.Exists(_paths.ConfigPath))
        {
            return CreateDefault();
        }

        try
        {
            ToolConfig? loaded = JsonSerializer.Deserialize<ToolConfig>(
                File.ReadAllText(_paths.ConfigPath),
                JsonDefaults.File);
            if (loaded == null)
            {
                return CreateDefault();
            }

            if (string.IsNullOrWhiteSpace(loaded.EvidenceDir))
            {
                loaded.EvidenceDir = Path.Combine(_paths.DataRoot, "evidence");
            }

            return loaded;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "配置读取失败，使用默认值。");
            return CreateDefault();
        }
    }

    /// <summary>创建默认配置。</summary>
    private ToolConfig CreateDefault() => new()
    {
        EvidenceDir = Path.Combine(_paths.DataRoot, "evidence"),
        UpdatedAt = DateTimeOffset.Now
    };

    /// <summary>原子写入配置。</summary>
    private void Write(ToolConfig config)
    {
        string temporary = _paths.ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, JsonDefaults.File));
        if (File.Exists(_paths.ConfigPath))
        {
            File.Delete(_paths.ConfigPath);
        }

        File.Move(temporary, _paths.ConfigPath);
    }

    /// <summary>规范化路径；空值保持为空。</summary>
    private static string NormalizePath(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value.Trim());

    /// <summary>复制配置，避免外部修改。</summary>
    private static ToolConfig Clone(ToolConfig source) => new()
    {
        ExePath = source.ExePath,
        RecipeDir = source.RecipeDir,
        EvidenceDir = source.EvidenceDir,
        ExpectedVersion = source.ExpectedVersion,
        BuildDate = source.BuildDate,
        TemplateRecipeName = source.TemplateRecipeName,
        UserName = source.UserName,
        ConfigurationRoot = source.ConfigurationRoot,
        Options = new RunOptions
        {
            SkipVersionAssertion = source.Options.SkipVersionAssertion,
            HardwarePolicy = source.Options.HardwarePolicy
        },
        UpdatedAt = source.UpdatedAt
    };
}
