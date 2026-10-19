using BlazingQuartz;
using BlazingQuartz.Core;
using Microsoft.OpenApi;
using MudBlazor.Services;
using Ray.BiliBiliTool.Config.SQLite;
using Ray.BiliBiliTool.Infrastructure;
using Ray.BiliBiliTool.Infrastructure.EF;
using Ray.BiliBiliTool.Infrastructure.EF.Extensions;
using Ray.BiliBiliTool.Web.Components;
using Ray.BiliBiliTool.Web.Extensions;
using Ray.BiliBiliTool.Web.Services.Pages.BiliAccount;
using Serilog;
using Serilog.Debugging;
using System.Reflection;

SelfLog.Enable(Console.Error);
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

// 推送通道包（Ray.Serilog.Sinks.*，v0.1.6）编译时引用 Ray.Infrastructure 0.0.26，
// 而仓库出于 NU1902 安全修复（见 common.props）将 Ray.Infrastructure 钉在 0.1.2。
// .NET Core 不做版本统一，导致任意推送通道真正发消息时抛
// “Could not load file or assembly 'Ray.Infrastructure, Version=0.0.26.0'”。
// 这里把该请求桥接到当前可用的 Ray.Infrastructure 程序集（0.1.2 为 0.0.26 的后向兼容小版本）。
AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var requested = new AssemblyName(args.Name);
    if (
        requested.Name == "Ray.Infrastructure"
        && requested.Version is { } requestedVersion
        && requestedVersion < new Version(0, 1, 0)
    )
    {
        return
            AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Ray.Infrastructure")
            ?? Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Ray.Infrastructure.dll"));
    }

    return null;
};

try
{
    var builder = WebApplication.CreateBuilder(args);

    var sqliteConnStr = builder.Configuration.GetConnectionString("Sqlite");
    if (!string.IsNullOrEmpty(sqliteConnStr))
    {
        builder.Configuration.AddSqlite(
            connectionString: sqliteConnStr,
            tableName: Ray.BiliBiliTool.Config.Constants.SqliteTableName,
            keyColumnName: "Key",
            valueColumnName: "Value"
        );
        BiliAccountPageWorkflow.CompactStoredAccounts(builder.Configuration);
    }

    builder
        .Services.AddRazorComponents()
        .AddInteractiveServerComponents()
        .AddInteractiveWebAssemblyComponents();
    builder.Services.AddControllers();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "BiliBiliToolPro API",
                Version = "v1",
                Description = "BiliBiliToolPro的API接口文档",
                Contact = new OpenApiContact
                {
                    Name = "BiliBiliToolPro",
                    Url = new Uri("https://github.com/RayWangQvQ/BiliBiliToolPro"),
                },
            }
        );
    });

    builder.Services.AddMudServices();

    builder.Services.AddEF();

    builder.Services.AddSerilog(
        (services, lc) =>
            lc
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.SQLite(
                    sqliteDbPath: sqliteConnStr?.Split(';')[0].Split('=')[1],
                    tableName: "bili_logs",
                    storeTimestampInUtc: true,
                    batchSize: 7
                )
    );

    // Add BlazingQuartz
    builder.Services.Configure<BlazingQuartzUIOptions>(
        builder.Configuration.GetSection("BlazingQuartz")
    );
    builder.Services.AddBlazingQuartz();
    builder.Services.AddBiliScheduler(builder.Configuration);

    builder
        .Services.AddWebServices()
        .AddAuthServices()
        .AddCoreModuleServices(builder.Configuration);

    var app = builder.Build();

    Global.ServiceProviderRoot = app.Services;
    await app.InitializeBiliToolAsync();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    app.UseStaticFiles();
    app.MapStaticAssets();
    app.UseAntiforgery();

    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(Ray.BiliBiliTool.Web.Client._Imports).Assembly);

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BiliBiliToolPro API V1");
        c.RoutePrefix = "swagger";
    });

    app.Run();
}
catch (Exception ex)
{
    // 记完日志后必须重新抛出，让进程以非 0 退出码结束。
    // 否则顶层语句正常返回、退出码为 0，Docker / 青龙 / SCF 等编排层会把
    // 「启动崩溃」当成「正常退出」：不重启、不告警，只是端口从未监听。
    // 退出码由运行时按未处理异常决定（Linux 134，Windows 0xE0434352），
    // 详见 docs/adr/0001-web-startup-failure-must-exit-nonzero.md。
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
