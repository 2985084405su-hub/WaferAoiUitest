using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using WaferAoi.UiTestTool.Web.Runs;
using WaferAoi.UiTestTool.Web.Storage;
using WaferAoi.UiTestTool.Web.Validation;

namespace WaferAoi.UiTestTool.Web;

/// <summary>
/// 本机 Web 宿主入口。只监听回环地址，执行器与 Web 分属两个进程；
/// 页面断开不取消运行，机器锁与待现场处理状态持久化到本机数据目录。
/// </summary>
internal static class Program
{
    /// <summary>启动宿主。</summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>异步任务。</returns>
    public static async Task Main(string[] args)
    {
        WebArguments arguments = WebArguments.Parse(args);
        LocalPaths paths = LocalPaths.Create(arguments);
        Directory.CreateDirectory(paths.DataRoot);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(arguments.Port));

        // 请求绑定与响应使用同一套 camelCase + 字符串枚举约定，避免前后端枚举形态不一致。
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        builder.Services.AddSingleton(arguments);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<ConfigStore>();
        builder.Services.AddSingleton<RunStore>();
        builder.Services.AddSingleton<MachineLockStore>();
        builder.Services.AddSingleton<ConfigValidator>();
        builder.Services.AddSingleton<RunManager>();
        builder.Services.AddSingleton<ExecutorHost>();

        WebApplication app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseMiddleware<OriginGuardMiddleware>();

        RunManager manager = app.Services.GetRequiredService<RunManager>();
        await manager.InitializeAsync().ConfigureAwait(false);

        app.MapApiEndpoints();

        if (Directory.Exists(paths.WebRoot))
        {
            var provider = new PhysicalFileProvider(paths.WebRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
            app.MapFallback(async context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(Path.Combine(paths.WebRoot, "index.html"));
            });
        }

        Console.WriteLine($"主流程回归工具已启动：http://127.0.0.1:{arguments.Port}");
        Console.WriteLine($"数据目录：{paths.DataRoot}");
        Console.WriteLine($"前端目录：{paths.WebRoot}（存在={Directory.Exists(paths.WebRoot)}）");
        Console.WriteLine($"执行器：{paths.ExecutorPath}（存在={File.Exists(paths.ExecutorPath)}）");

        await app.RunAsync().ConfigureAwait(false);
    }
}
