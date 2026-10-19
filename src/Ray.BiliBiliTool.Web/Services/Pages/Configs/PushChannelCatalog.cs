using MudBlazor;

namespace Ray.BiliBiliTool.Web.Services.Pages.Configs;

/// <summary>
/// How a push-channel argument is rendered and validated on the push-config page.
/// </summary>
public enum PushFieldKind
{
    Text,
    Password,
    TextArea,
    Select,
}

/// <summary>
/// One configurable argument of a Serilog push sink (for example <c>botToken</c> of TelegramBatched).
/// </summary>
public sealed record PushFieldDefinition(
    string Key,
    string Label,
    PushFieldKind Kind = PushFieldKind.Text,
    bool Required = false,
    string? Placeholder = null,
    string? HelperText = null,
    string[]? Options = null,
    string? DefaultValue = null
);

/// <summary>
/// One push channel, i.e. one entry of the <c>Serilog:WriteTo</c> array in appsettings.json.
/// </summary>
public sealed record PushChannelDefinition(
    int Index,
    string SinkName,
    string DisplayName,
    string Description,
    string Icon,
    IReadOnlyList<PushFieldDefinition> Fields,
    string? DocUrl = null,
    Func<IReadOnlyDictionary<string, string>, bool>? IsConfigured = null
)
{
    /// <summary>
    /// Configuration prefix that stores this channel's args, e.g. <c>Serilog:WriteTo:3:Args:</c>.
    /// </summary>
    public string ConfigPrefix => $"Serilog:WriteTo:{Index}:Args:";

    /// <summary>
    /// True when every field needed to deliver messages is non-empty. Used for the 已配置/未配置 badge.
    /// </summary>
    public bool IsChannelConfigured(IReadOnlyDictionary<string, string> values) =>
        IsConfigured?.Invoke(values)
        ?? Fields.Where(field => field.Required).All(field => !string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Key)));
}

/// <summary>
/// Catalog of every push channel the web panel can manage.
///
/// Indexes must match the order of the Serilog sinks in <c>Serilog:WriteTo</c> of
/// <c>src/Ray.BiliBiliTool.Web/appsettings.json</c> (3 = Telegram, 12 = Gotify, 13 = 飞书机器人,
/// 14 = Bark, 15 = PushDeer; 0-2 are Console/Debug/File and are not user-configurable channels).
/// </summary>
public static class PushChannelCatalog
{
    public static IReadOnlyList<PushChannelDefinition> All { get; } = Build();

    private static IReadOnlyList<PushChannelDefinition> Build() =>
        new[]
        {
            new PushChannelDefinition(
                Index: 3,
                SinkName: "TelegramBatched",
                DisplayName: "Telegram 机器人",
                Description:
                    "通过 Telegram Bot API 推送消息。先在 @BotFather 创建机器人拿到 token，再给机器人发一条消息，"
                        + "访问 https://api.telegram.org/bot{TOKEN}/getUpdates 获取 chatId。",
                Icon: Icons.Material.Filled.Send,
                DocUrl: "https://core.telegram.org/bots/api",
                Fields:
                [
                    new PushFieldDefinition(
                        "botToken",
                        "Bot Token",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "BotFather 创建的机器人 Token。"
                    ),
                    new PushFieldDefinition(
                        "chatId",
                        "Chat ID",
                        Required: true,
                        HelperText: "接收消息的会话 ID，可从 getUpdates 获取。"
                    ),
                    new PushFieldDefinition(
                        "proxy",
                        "代理",
                        Placeholder: "user:password@host:port",
                        HelperText: "可选，Telegram 需要代理时填写。"
                    ),
                    new PushFieldDefinition(
                        "apiHost",
                        "API 地址",
                        DefaultValue: "https://api.telegram.org",
                        HelperText: "可替换为自建反代地址。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 4,
                SinkName: "WorkWeiXinBatched",
                DisplayName: "企业微信机器人",
                Description: "向企业微信群机器人推送消息。在群设置中添加机器人后复制 Webhook 地址。",
                Icon: Icons.Material.Filled.Work,
                DocUrl: "https://work.weixin.qq.com/api/doc/90000/90136/91770",
                Fields:
                [
                    new PushFieldDefinition(
                        "webHookUrl",
                        "Webhook 地址",
                        Required: true,
                        HelperText: "群机器人生成的 Webhook URL。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 5,
                SinkName: "DingTalkBatched",
                DisplayName: "钉钉机器人",
                Description: "向钉钉群机器人推送消息。群机器人 Webhook 需使用关键字安全设置（如 Ray 或 BiliBili），不支持加签。",
                Icon: Icons.Material.Filled.NotificationsActive,
                DocUrl: "https://open.dingtalk.com/document/robots/custom-robot-access",
                Fields:
                [
                    new PushFieldDefinition(
                        "webHookUrl",
                        "Webhook 地址",
                        Required: true,
                        HelperText: "钉钉群自定义机器人 Webhook URL。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 6,
                SinkName: "ServerChanBatched",
                DisplayName: "Server酱",
                Description: "通过 Server酱 推送到微信。填写 turboScKey（推荐）或旧版 scKey 任一即可。",
                Icon: Icons.Material.Filled.Email,
                DocUrl: "https://sct.ftqq.com/",
                IsConfigured: values =>
                    !string.IsNullOrWhiteSpace(values.GetValueOrDefault("turboScKey"))
                    || !string.IsNullOrWhiteSpace(values.GetValueOrDefault("scKey")),
                Fields:
                [
                    new PushFieldDefinition(
                        "turboScKey",
                        "turboScKey（推荐）",
                        PushFieldKind.Password,
                        HelperText: "Server酱³ 平台生成的 SendKey。"
                    ),
                    new PushFieldDefinition(
                        "scKey",
                        "scKey（旧版）",
                        PushFieldKind.Password,
                        HelperText: "旧版 Server酱 的 SCKEY，与 turboScKey 至少填一个。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 7,
                SinkName: "CoolPushBatched",
                DisplayName: "酷推",
                Description:
                    "通过酷推推送到 QQ/微信。注意：该渠道曾被反馈 API 行为不稳定、存在封号风险，不推荐使用。",
                Icon: Icons.Material.Filled.LocalFireDepartment,
                DocUrl: "https://cp.xuthus.cc/",
                Fields:
                [
                    new PushFieldDefinition(
                        "sKey",
                        "SKey",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "酷推平台生成的 SKey。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 8,
                SinkName: "OtherApiBatched",
                DisplayName: "自定义 API",
                Description: "将日志 POST 到你自己的 API 或机器人接口，支持占位符与 JSON 模板。",
                Icon: Icons.Material.Filled.Code,
                Fields:
                [
                    new PushFieldDefinition(
                        "api",
                        "API 地址",
                        Required: true,
                        Placeholder: "https://example.com/push",
                        HelperText: "接收消息的接口地址。"
                    ),
                    new PushFieldDefinition(
                        "placeholder",
                        "占位符",
                        DefaultValue: "#msg#",
                        HelperText: "消息内容在模板中的占位符。"
                    ),
                    new PushFieldDefinition(
                        "bodyJsonTemplate",
                        "请求体 JSON 模板",
                        PushFieldKind.TextArea,
                        DefaultValue: """{"msgtype":"markdown","markdown":{"content":#msg#}}""",
                        HelperText: "作为 POST 请求体；占位符会被 JSON 编码后的日志内容替换（本身已带引号，模板中不要再用引号包裹）。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 9,
                SinkName: "PushPlusBatched",
                DisplayName: "PushPlus（推荐）",
                Description: "通过 PushPlus 推送到微信等渠道，国内网络友好。",
                Icon: Icons.Material.Filled.Campaign,
                DocUrl: "https://www.pushplus.plus/doc/",
                IsConfigured: values =>
                    !string.IsNullOrWhiteSpace(values.GetValueOrDefault("token")),
                Fields:
                [
                    new PushFieldDefinition(
                        "token",
                        "Token",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "PushPlus 平台生成的 Token。"
                    ),
                    new PushFieldDefinition(
                        "channel",
                        "渠道",
                        PushFieldKind.Select,
                        Options: ["wechat", "webhook", "cp", "sms", "mail"],
                        HelperText: "wechat=微信公众号，webhook=第三方 webhook，cp=企业微信应用，sms=短信，mail=邮件。留空默认 wechat。"
                    ),
                    new PushFieldDefinition(
                        "topic",
                        "群组编码 Topic",
                        HelperText: "用于群发，留空仅发送给自己；channel 为 webhook 时无效。"
                    ),
                    new PushFieldDefinition(
                        "webhook",
                        "Webhook 编码",
                        HelperText: "webhook 编码（非地址），仅 channel 为 webhook / cp 时填写。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 10,
                SinkName: "MicrosoftTeamsBatched",
                DisplayName: "Microsoft Teams",
                Description: "向 Teams 频道传入 Webhook 推送消息。",
                Icon: Icons.Material.Filled.Groups,
                DocUrl: "https://learn.microsoft.com/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook",
                Fields:
                [
                    new PushFieldDefinition(
                        "webhook",
                        "Webhook 地址",
                        Required: true,
                        HelperText: "Teams 频道生成的完整 Webhook URL。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 11,
                SinkName: "WorkWeiXinAppBatched",
                DisplayName: "企业微信应用",
                Description: "通过企业微信自建应用向成员推送消息，corpId、agentId、secret 必填，toUser/toParty/toTag 至少填一个。",
                Icon: Icons.Material.Filled.Business,
                DocUrl: "https://developer.work.weixin.qq.com/tutorial/application-message",
                IsConfigured: values =>
                    !string.IsNullOrWhiteSpace(values.GetValueOrDefault("corpId"))
                    && !string.IsNullOrWhiteSpace(values.GetValueOrDefault("agentId"))
                    && !string.IsNullOrWhiteSpace(values.GetValueOrDefault("secret"))
                    && (
                        !string.IsNullOrWhiteSpace(values.GetValueOrDefault("toUser"))
                        || !string.IsNullOrWhiteSpace(values.GetValueOrDefault("toParty"))
                        || !string.IsNullOrWhiteSpace(values.GetValueOrDefault("toTag"))
                    ),
                Fields:
                [
                    new PushFieldDefinition("corpId", "Corp ID", Required: true),
                    new PushFieldDefinition("agentId", "Agent ID", Required: true),
                    new PushFieldDefinition("secret", "Secret", PushFieldKind.Password, Required: true),
                    new PushFieldDefinition("toUser", "接收人 ToUser", DefaultValue: "@all"),
                    new PushFieldDefinition("toParty", "接收部门 ToParty"),
                    new PushFieldDefinition("toTag", "接收标签 ToTag"),
                ]
            ),
            new PushChannelDefinition(
                Index: 12,
                SinkName: "GotifyBatched",
                DisplayName: "Gotify",
                Description: "向自建 Gotify 服务器推送消息，需要服务器地址与应用 Token。",
                Icon: Icons.Material.Filled.Notifications,
                DocUrl: "https://gotify.net/",
                Fields:
                [
                    new PushFieldDefinition(
                        "host",
                        "服务器地址",
                        Required: true,
                        Placeholder: "https://gotify.example.com",
                        HelperText: "Gotify 服务器地址。"
                    ),
                    new PushFieldDefinition(
                        "token",
                        "应用 Token",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "Gotify 应用（App）的 Token。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 13,
                SinkName: "FeishuPush",
                DisplayName: "飞书机器人",
                Description: "向飞书群机器人推送消息。在群设置中添加自定义机器人后复制 Webhook 地址（如 https://open.feishu.cn/open-apis/bot/v2/hook/xxx）。",
                Icon: Icons.Material.Filled.Send,
                DocUrl: "https://open.feishu.cn/document/client-docs/bot-v3/add-custom-bot",
                Fields:
                [
                    new PushFieldDefinition(
                        "webHookUrl",
                        "Webhook 地址",
                        Required: true,
                        Placeholder: "https://open.feishu.cn/open-apis/bot/v2/hook/xxx",
                        HelperText: "飞书群自定义机器人的 Webhook URL。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 14,
                SinkName: "BarkPush",
                DisplayName: "Bark",
                Description: "通过 Bark 推送到 iOS 设备。在 iOS 安装 Bark App 后取得设备 Key，可自建服务器或用官方 https://api.day.app。",
                Icon: Icons.Material.Filled.Notifications,
                DocUrl: "https://github.com/Finb/Bark",
                IsConfigured: values =>
                    !string.IsNullOrWhiteSpace(values.GetValueOrDefault("deviceKey")),
                Fields:
                [
                    new PushFieldDefinition(
                        "server",
                        "服务器地址",
                        DefaultValue: "https://api.day.app",
                        HelperText: "Bark 服务器地址，默认官方 https://api.day.app。"
                    ),
                    new PushFieldDefinition(
                        "deviceKey",
                        "设备 Key",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "Bark App 中显示的设备 Key。"
                    ),
                ]
            ),
            new PushChannelDefinition(
                Index: 15,
                SinkName: "PushDeerPush",
                DisplayName: "PushDeer",
                Description: "通过 PushDeer 推送到微信/App。使用 pushkey 推送，可自建服务器或用官方 https://api2.pushdeer.com。",
                Icon: Icons.Material.Filled.Email,
                DocUrl: "https://www.pushdeer.com",
                IsConfigured: values =>
                    !string.IsNullOrWhiteSpace(values.GetValueOrDefault("pushkey")),
                Fields:
                [
                    new PushFieldDefinition(
                        "endpoint",
                        "服务器地址",
                        DefaultValue: "https://api2.pushdeer.com",
                        HelperText: "PushDeer 服务器地址，默认官方 https://api2.pushdeer.com。"
                    ),
                    new PushFieldDefinition(
                        "pushkey",
                        "PushKey",
                        PushFieldKind.Password,
                        Required: true,
                        HelperText: "PushDeer 平台生成的 PushKey。"
                    ),
                ]
            ),
        };
}
