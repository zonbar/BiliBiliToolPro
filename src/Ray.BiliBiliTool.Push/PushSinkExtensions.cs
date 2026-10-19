using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace Ray.BiliBiliTool.Push;

/// <summary>
/// Serilog sink extension methods for the built-in push channels (Feishu bot, Bark, PushDeer).
///
/// They are discovered by <c>Serilog.Settings.Configuration</c> because the Web host lists
/// <c>Ray.BiliBiliTool.Web</c> in <c>Serilog:Using</c>, and their args are bound from the
/// <c>Serilog:WriteTo:{index}:Args</c> section (persisted by the push-config page).
/// </summary>
public static class PushSinkExtensions
{
    /// <summary>
    /// 飞书群机器人：POST <see cref="webHookUrl"/>，消息体为 text 类型。
    /// </summary>
    public static LoggerConfiguration FeishuPush(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        string webHookUrl,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum
    ) =>
        loggerSinkConfiguration.Sink(
            new HttpBatchedPushSink(
                (message, ct) =>
                    PushHttp.PostJsonAsync(
                        webHookUrl,
                        new { msg_type = "text", content = new { text = message } },
                        ct
                    ),
                () => !string.IsNullOrWhiteSpace(webHookUrl)
            ),
            restrictedToMinimumLevel
        );

    /// <summary>
    /// Bark（iOS 推送）：POST 到 <c>{server}/{deviceKey}</c>，消息体为 JSON。
    /// </summary>
    public static LoggerConfiguration BarkPush(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        string server = "https://api.day.app",
        string deviceKey = "",
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum
    ) =>
        loggerSinkConfiguration.Sink(
            new HttpBatchedPushSink(
                (message, ct) =>
                    PushHttp.PostJsonAsync(
                        $"{server.TrimEnd('/')}/{Uri.EscapeDataString(deviceKey)}",
                        new { title = "BiliTool", body = message },
                        ct
                    ),
                () => !string.IsNullOrWhiteSpace(deviceKey)
            ),
            restrictedToMinimumLevel
        );

    /// <summary>
    /// PushDeer：POST 到 <c>{endpoint}/message/push</c>，markdown 类型。
    /// </summary>
    public static LoggerConfiguration PushDeerPush(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        string endpoint = "https://api2.pushdeer.com",
        string pushkey = "",
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum
    ) =>
        loggerSinkConfiguration.Sink(
            new HttpBatchedPushSink(
                (message, ct) =>
                    PushHttp.PostJsonAsync(
                        $"{endpoint.TrimEnd('/')}/message/push",
                        new
                        {
                            pushkey,
                            text = "BiliTool",
                            desp = message,
                            type = "markdown",
                        },
                        ct
                    ),
                () => !string.IsNullOrWhiteSpace(pushkey)
            ),
            restrictedToMinimumLevel
        );
}

internal static class PushHttp
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task PostJsonAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
