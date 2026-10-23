using System.Globalization;
using System.Security.Principal;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Web.Validation;

/// <summary>
/// 启动前配置检查。检查结果只作为运行准备记录，不虚构为用例步骤通过；
/// 版本号与编译日期是否必填由本轮“跳过版本断言”选项决定。
/// </summary>
internal sealed class ConfigValidator
{
    /// <summary>校验配置。</summary>
    /// <param name="config">待校验配置。</param>
    /// <param name="skipVersionAssertion">本轮是否跳过版本断言。</param>
    /// <param name="executorPath">执行器路径；为空表示尚未解析。</param>
    /// <returns>校验结果。</returns>
    public ConfigValidationResult Validate(
        ToolConfig config,
        bool skipVersionAssertion,
        string? executorPath)
    {
        var result = new ConfigValidationResult
        {
            CheckedAt = DateTimeOffset.Now,
            Valid = true
        };

        CheckExecutable(config.ExePath, result);
        CheckDirectory("recipeDir", "配方目录", config.RecipeDir, mustExist: true, result);
        CheckDirectory("evidenceDir", "证据目录", config.EvidenceDir, mustExist: false, result);
        CheckVersion(config, skipVersionAssertion, result);
        CheckDirectory("configurationRoot", "生产配置目录", config.ConfigurationRoot, mustExist: true, result, onlyWarning: true);
        CheckExecutor(executorPath, result);
        CheckPassword(result);
        CheckAdministrator(result);

        result.Valid = result.Issues.Count == 0;
        return result;
    }

    /// <summary>被测程序检查。</summary>
    private static void CheckExecutable(string path, ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AddIssue(result, "exePath", "required", "被测程序 EXE 路径不能为空。");
            result.Checks.Add(new PreparationCheck
            {
                Id = "exe",
                Label = "被测程序",
                Status = "failed",
                Detail = "未配置 EXE 路径。"
            });
            return;
        }

        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, "exePath", "not_executable", "被测程序路径必须以 .exe 结尾。");
        }
        else if (!File.Exists(path))
        {
            AddIssue(result, "exePath", "not_found", $"找不到被测程序：{path}");
        }
        else
        {
            result.Checks.Add(new PreparationCheck
            {
                Id = "exe",
                Label = "被测程序",
                Status = "ok",
                Detail = path
            });
        }
    }

    /// <summary>目录检查。</summary>
    private static void CheckDirectory(
        string field,
        string label,
        string path,
        bool mustExist,
        ConfigValidationResult result,
        bool onlyWarning = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AddIssue(result, field, "required", $"{label}不能为空。");
            result.Checks.Add(new PreparationCheck
            {
                Id = field,
                Label = label,
                Status = "failed",
                Detail = "未配置。"
            });
            return;
        }

        try
        {
            if (mustExist && !Directory.Exists(path))
            {
                if (onlyWarning)
                {
                    result.Checks.Add(new PreparationCheck
                    {
                        Id = field,
                        Label = label,
                        Status = "warning",
                        Detail = $"目录不存在：{path}"
                    });
                    return;
                }

                AddIssue(result, field, "not_found", $"{label}不存在：{path}");
                return;
            }

            if (!mustExist && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            result.Checks.Add(new PreparationCheck
            {
                Id = field,
                Label = label,
                Status = "ok",
                Detail = path
            });
        }
        catch (Exception exception)
        {
            if (onlyWarning)
            {
                result.Checks.Add(new PreparationCheck
                {
                    Id = field,
                    Label = label,
                    Status = "warning",
                    Detail = $"{label}不可访问：{exception.Message}"
                });
                return;
            }

            AddIssue(result, field, "not_writable", $"{label}不可访问：{exception.Message}");
        }
    }

    /// <summary>版本号与编译日期检查。</summary>
    private static void CheckVersion(
        ToolConfig config,
        bool skipVersionAssertion,
        ConfigValidationResult result)
    {
        if (skipVersionAssertion)
        {
            result.Checks.Add(new PreparationCheck
            {
                Id = "version",
                Label = "版本断言",
                Status = "warning",
                Detail = "本轮选择跳过版本断言；01.02 将记为跳过，不校验版本号与编译日期。"
            });
            return;
        }

        bool valid = true;
        if (string.IsNullOrWhiteSpace(config.ExpectedVersion))
        {
            AddIssue(result, "expectedVersion", "required", "未勾选“跳过版本断言”时，预期版本号必填。");
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(config.BuildDate))
        {
            AddIssue(result, "buildDate", "required", "未勾选“跳过版本断言”时，预期编译日期必填。");
            valid = false;
        }
        else if (!TryNormalizeBuildDate(config.BuildDate, out _))
        {
            AddIssue(result, "buildDate", "invalid_format", "编译日期格式应为 yyyy-MM-dd HH:mm 或 yyyy-MM-dd。");
            valid = false;
        }

        result.Checks.Add(new PreparationCheck
        {
            Id = "version",
            Label = "版本断言",
            Status = valid ? "ok" : "failed",
            Detail = valid
                ? $"预期版本 {config.ExpectedVersion}，编译日期 {config.BuildDate}。"
                : "版本号或编译日期不满足必填与格式要求。"
        });
    }

    /// <summary>执行器检查。</summary>
    private static void CheckExecutor(string? executorPath, ConfigValidationResult result)
    {
        bool exists = !string.IsNullOrWhiteSpace(executorPath) && File.Exists(executorPath);
        result.Checks.Add(new PreparationCheck
        {
            Id = "executor",
            Label = "桌面执行器",
            Status = exists ? "ok" : "failed",
            Detail = exists ? executorPath! : "找不到执行器；请用 --executor 指定绝对路径。"
        });

        if (!exists)
        {
            AddIssue(result, "executor", "not_found", "找不到桌面执行器可执行文件。");
        }
    }

    /// <summary>密码存在性检查；不读取、不回显密码值。</summary>
    private static void CheckPassword(ConfigValidationResult result)
    {
        bool configured = Storage.ConfigStore.PasswordConfigured();
        result.Checks.Add(new PreparationCheck
        {
            Id = "password",
            Label = "登录密码",
            Status = configured ? "ok" : "warning",
            Detail = configured
                ? "已在测试机环境变量中配置；页面不回传密码。"
                : "未在 WAFER_AOI_UI_PASSWORD 中配置密码，登录步骤可能失败。"
        });
    }

    /// <summary>管理员权限检查；执行器与被测程序必须处于可交互桌面。</summary>
    private static void CheckAdministrator(ConfigValidationResult result)
    {
        bool admin;
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            admin = false;
        }

        result.Checks.Add(new PreparationCheck
        {
            Id = "administrator",
            Label = "管理员权限",
            Status = admin ? "ok" : "warning",
            Detail = admin
                ? "当前进程具备管理员权限。"
                : "当前进程不是管理员；UIA 操作与启动被测程序会失败。"
        });
    }

    /// <summary>规范化编译日期；支持带时分的安装记录和仅日期格式。</summary>
    /// <param name="value">原始值。</param>
    /// <param name="normalized">规范化后的 yyyy-MM-dd HH:mm。</param>
    /// <returns>格式有效时为 true。</returns>
    public static bool TryNormalizeBuildDate(string value, out string normalized)
    {
        normalized = string.Empty;
        string[] formats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            normalized = parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    /// <summary>追加字段错误。</summary>
    private static void AddIssue(ConfigValidationResult result, string field, string code, string message) =>
        result.Issues.Add(new ValidationIssue { Field = field, Code = code, Message = message });
}
