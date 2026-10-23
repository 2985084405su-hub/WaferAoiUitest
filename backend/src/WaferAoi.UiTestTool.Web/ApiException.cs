using System.Text.Json;
using WaferAoi.UiTestTool.Contracts;

namespace WaferAoi.UiTestTool.Web;

/// <summary>携带 HTTP 状态码的 API 异常；由中间件统一输出错误体。</summary>
internal sealed class ApiException : Exception
{
    /// <summary>创建 API 异常。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="code">机器可读错误码。</param>
    /// <param name="message">可展示消息。</param>
    /// <param name="issues">字段级错误。</param>
    /// <param name="currentRun">相关运行快照。</param>
    public ApiException(
        int statusCode,
        string code,
        string message,
        List<ValidationIssue>? issues = null,
        RunSnapshot? currentRun = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Issues = issues;
        CurrentRun = currentRun;
    }

    /// <summary>HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>机器可读错误码。</summary>
    public string Code { get; }

    /// <summary>字段级错误。</summary>
    public List<ValidationIssue>? Issues { get; }

    /// <summary>相关运行。</summary>
    public RunSnapshot? CurrentRun { get; }
}

/// <summary>把 <see cref="ApiException"/> 与未处理异常转换为统一 JSON 错误体。</summary>
internal sealed class ApiExceptionMiddleware
{
    /// <summary>下一个中间件。</summary>
    private readonly RequestDelegate _next;

    /// <summary>日志。</summary>
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    /// <summary>创建中间件。</summary>
    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>执行请求。</summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException exception)
        {
            await WriteErrorAsync(context, exception.StatusCode, new ApiError
            {
                Code = exception.Code,
                Message = exception.Message,
                Issues = exception.Issues,
                CurrentRun = exception.CurrentRun
            });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 浏览器断开不改变运行状态，也不需要错误响应。
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "未处理异常：{Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, new ApiError
            {
                Code = "internal_error",
                Message = "宿主处理请求失败：" + exception.Message
            });
        }
    }

    /// <summary>写出 JSON 错误体。</summary>
    private static async Task WriteErrorAsync(HttpContext context, int statusCode, ApiError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(error, JsonDefaults.Api));
    }
}

/// <summary>
/// 同源保护。宿主只监听回环地址，浏览器提交仍要求 Origin 与本次请求同源；
/// 无 Origin 的本机命令行请求放行，便于现场联调。
/// </summary>
internal sealed class OriginGuardMiddleware
{
    /// <summary>下一个中间件。</summary>
    private readonly RequestDelegate _next;

    /// <summary>创建中间件。</summary>
    public OriginGuardMiddleware(RequestDelegate next) => _next = next;

    /// <summary>执行请求。</summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsMutating(context.Request.Method) && context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            if (!IsSameOrigin(origin.ToString(), context.Request.Host))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiError
                {
                    Code = "origin_rejected",
                    Message = "只接受本机同源页面提交。"
                }, JsonDefaults.Api));
                return;
            }
        }

        await _next(context);
    }

    /// <summary>是否修改状态的请求。</summary>
    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    /// <summary>判断 Origin 是否为回环同源。</summary>
    private static bool IsSameOrigin(string origin, HostString host)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        bool loopback = uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        if (!loopback)
        {
            return false;
        }

        int originPort = uri.Port;
        int requestPort = host.Port ?? (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80);
        return originPort == requestPort;
    }
}
