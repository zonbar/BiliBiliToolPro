using Ray.BiliBiliTool.Config.SQLite;
using global::Serilog;
using Ray.Serilog.Sinks.Batched;

namespace Ray.BiliBiliTool.Web.Services.Pages.Configs;

/// <summary>
/// Result of a test push.
/// </summary>
public sealed record PushTestResult(bool Success, string Message);

/// <summary>
/// 消息推送配置工作流：读取/保存各推送通道（Serilog sink）参数，并向指定通道发送测试消息。
/// </summary>
public interface IPushSettingsWorkflow
{
    /// <summary>
    /// 读取每个通道当前生效的参数（含 SQLite 覆盖层与 appsettings 默认值）。
    /// </summary>
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Read();

    /// <summary>
    /// 保存单个通道的参数到 SQLite 配置表并触发配置重载（Serilog 热更新，无需重启）。
    /// </summary>
    void Save(int channelIndex, IReadOnlyDictionary<string, string> values);

    /// <summary>
    /// 读取全局自定义消息标题（Notify:Title，含 SQLite 覆盖层与 appsettings 默认值）。
    /// </summary>
    string ReadTitle();

    /// <summary>
    /// 保存全局自定义消息标题到 SQLite 配置表并触发配置重载（留空则回退到默认）。
    /// </summary>
    void SaveTitle(string? title);

    /// <summary>
    /// 仅使用目标通道构建一个临时 Serilog 管道，发送一条测试消息。
    /// 返回成功仅代表消息已提交发送，请到目标应用确认是否送达。
    /// </summary>
    Task<PushTestResult> TestAsync(
        PushChannelDefinition channel,
        IReadOnlyDictionary<string, string> values
    );
}

public sealed class PushSettingsWorkflow(IConfiguration configuration)
    : IPushSettingsWorkflow
{
    private readonly IConfiguration _configuration = configuration;

    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Read()
    {
        var result = new Dictionary<int, IReadOnlyDictionary<string, string>>();
        foreach (var channel in PushChannelCatalog.All)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in channel.Fields)
            {
                values[field.Key] =
                    _configuration[$"{channel.ConfigPrefix}{field.Key}"]
                    ?? field.DefaultValue
                    ?? string.Empty;
            }
            result[channel.Index] = values;
        }
        return result;
    }

    public void Save(int channelIndex, IReadOnlyDictionary<string, string> values)
    {
        var channel =
            PushChannelCatalog.All.FirstOrDefault(item => item.Index == channelIndex)
            ?? throw new InvalidOperationException("未知的推送通道，请刷新页面后重试");

        var missing = channel.Fields
            .Where(field => field.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
            .Select(field => field.Label)
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"请先填写必填项：{string.Join("、", missing)}");

        var root = _configuration as IConfigurationRoot;
        var provider =
            root?.Providers.OfType<SqliteConfigurationProvider>().LastOrDefault()
            ?? throw new InvalidOperationException("保存失败，请稍后重试");

        var configValues = new Dictionary<string, string>();
        var keysToDelete = new List<string>();
        foreach (var field in channel.Fields)
        {
            var value = values.GetValueOrDefault(field.Key);
            var key = $"{channel.ConfigPrefix}{field.Key}";
            if (string.IsNullOrWhiteSpace(value))
            {
                // 清空用户输入时，删除 SQLite 覆盖键，回退到 appsettings 默认值。
                keysToDelete.Add(key);
            }
            else
            {
                configValues[key] = value;
            }
        }
        provider.BatchSet(configValues, keysToDelete);
        root!.Reload();
    }

    public string ReadTitle() => _configuration["Notify:Title"] ?? string.Empty;

    public void SaveTitle(string? title)
    {
        var root =
            _configuration as IConfigurationRoot
            ?? throw new InvalidOperationException("保存失败，请稍后重试");
        var provider =
            root.Providers.OfType<SqliteConfigurationProvider>().LastOrDefault()
            ?? throw new InvalidOperationException("保存失败，请稍后重试");

        if (string.IsNullOrWhiteSpace(title))
        {
            // 留空则删除 SQLite 覆盖键，回退到 appsettings 默认（行为与未设置一致）。
            provider.BatchSet(new Dictionary<string, string>(), ["Notify:Title"]);
        }
        else
        {
            provider.BatchSet(
                new Dictionary<string, string> { ["Notify:Title"] = title.Trim() },
                []
            );
        }
        root.Reload();
    }

    public async Task<PushTestResult> TestAsync(
        PushChannelDefinition channel,
        IReadOnlyDictionary<string, string> values
    )
    {
        var missing = channel.Fields
            .Where(field => field.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)))
            .Select(field => field.Label)
            .ToArray();
        if (missing.Length > 0)
            return new PushTestResult(false, $"请先填写必填项：{string.Join("、", missing)}");

        // 用内存配置构建一条只包含目标通道的临时 Serilog 管道，避免干扰主日志管道。
        var tempValues = new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Information",
            ["Serilog:WriteTo:0:Name"] = channel.SinkName,
        };

        var usingList = _configuration.GetSection("Serilog:Using").Get<string[]>() ?? [];
        for (int i = 0; i < usingList.Length; i++)
            tempValues[$"Serilog:Using:{i}"] = usingList[i];

        foreach (var field in channel.Fields)
        {
            var value = values.GetValueOrDefault(field.Key);
            if (!string.IsNullOrWhiteSpace(value))
                tempValues[$"Serilog:WriteTo:0:Args:{field.Key}"] = value;
        }

        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(tempValues)
            .Build();

        global::Serilog.Core.Logger? testLogger = null;
        try
        {
            testLogger = new LoggerConfiguration()
                .ReadFrom.Configuration(testConfig)
                .CreateLogger();
            var message =
                $"[BiliTool 推送测试] 这是一条来自 BiliBiliToolPro Web 面板的测试消息。"
                + $"\n通道：{channel.DisplayName}"
                + $"\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                + $"\n如果收到本消息，说明推送通道配置正确。";
            testLogger.Warning(message);

            // 手动以用户配置的全局标题触发临时管道 flush，使测试消息的 title 字段也使用该标题
            // （与正式推送 BaseJob 的 flush 行为一致），而非各通道的默认标题。
            var testTitle = _configuration["Notify:Title"] ?? string.Empty;
            await BatchSinkManager.FlushAllAsync(testTitle);

            // 等待临时管道刷出批量消息；即使超时，发送也已在后台继续。
            await Task.WhenAny(
                Task.Run(testLogger.Dispose),
                Task.Delay(TimeSpan.FromSeconds(15))
            );

            return new PushTestResult(
                true,
                $"测试消息已通过「{channel.DisplayName}」通道发送，请在目标应用（如 Telegram / 微信 / 钉钉）中确认是否收到。"
            );
        }
        catch (Exception ex)
        {
            return new PushTestResult(false, $"发送失败：{ex.Message}");
        }
        finally
        {
            // 正常路径 dispose 已执行；超时路径下再次 dispose 是幂等的安全操作。
            testLogger?.Dispose();
        }
    }
}
