using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaferAoi.UiTestTool.Web;

/// <summary>Web 宿主与文件读写的 JSON 约定。</summary>
internal static class JsonDefaults
{
    /// <summary>接口输出：camelCase、字符串枚举、省略 null。</summary>
    public static readonly JsonSerializerOptions Api = Create(false);

    /// <summary>落盘文件：与接口同构，便于现场直接查看。</summary>
    public static readonly JsonSerializerOptions File = Create(true);

    /// <summary>事件行：events.jsonl 一行一条，不能缩进，否则按行重放会丢事件。</summary>
    public static readonly JsonSerializerOptions Line = Create(false);

    /// <summary>创建选项。</summary>
    /// <param name="indented">是否缩进。</param>
    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
