# 消息推送通道管理：Web 后台配置 + 测试，新增飞书/Bark/PushDeer（Web 与 Console 双主机）

## 概述

BiliBiliToolPro 的消息推送全部经由 Serilog 日志管道转发到 10 个第三方推送 Sink（Telegram、企业微信机器人、钉钉、Server酱、酷推、自定义 API、PushPlus、Microsoft Teams、企业微信应用、Gotify）。此前通道参数只能手改 `appsettings.json` 或环境变量，Web 后台没有任何配置入口，也没有任何手段验证通道是否真的能用。

本 PR 在 Web 后台新增「消息推送」管理页，补齐通道配置与测试能力；**新增 3 个内置推送渠道（飞书机器人、Bark、PushDeer，共 13 个可管理通道）**；并顺带修复了一个导致**所有推送通道真实发送必然失败**的上游缺陷。

## 变更内容

### 新增

- `src/Ray.BiliBiliTool.Web/Components/Pages/Configs/PushConfig.razor`
  新的推送配置页面（路由 `/Configurations/PushConfig`，侧边栏「任务配置 → 消息推送」）。
  - 13 个推送通道以折叠面板列出，含名称、说明、官方文档链接、已配置/未配置状态徽章
  - 每个通道可填写其全部参数（文本 / 密码 / 多行 / 下拉四种控件），必填项以 `*` 标注并校验
  - 每通道「保存」（写入 SQLite 配置表并触发 Serilog 热更新，**保存即生效，无需重启**）与「发送测试消息」按钮
- `src/Ray.BiliBiliTool.Web/Services/Pages/Configs/PushChannelCatalog.cs`
  通道目录定义：每个通道对应 `Serilog:WriteTo` 数组的固定下标（3–15），声明字段、必填规则、默认值、自定义"已配置"判定（如 Server酱 二选一、企业微信应用需收件人三选一、Bark/PushDeer 需 deviceKey/pushkey 非空）。
- `src/Ray.BiliBiliTool.Web/Services/Pages/Configs/PushSettingsWorkflow.cs`
  工作流：
  - `Read()`：读取各通道当前生效参数（SQLite 覆盖层 + appsettings 默认值）
  - `Save()`：校验必填项，批量写入 SQLite（空值删除覆盖键、回退默认值），`Reload()` 触发热更新
  - `TestAsync()`：仅用目标通道构建一条临时 Serilog 管道发送测试消息（15 秒超时保护），不污染主日志管道、不影响其他通道
- `src/Ray.BiliBiliTool.Push/`（新共享类库，Web 与 Console 双主机共用）
  - `HttpBatchedPushSink.cs`：自研批量 HTTP 推送 sink（`ILogEventSink`）：聚合日志消息，每 10s 一次性 POST 发出一条；dispose 时同步冲刷，保证测试消息即时发出；未配置（参数为空）时静默跳过，不产生无效调用。
  - `PushSinkExtensions.cs`：三个 Serilog sink 扩展方法（`FeishuPush` / `BarkPush` / `PushDeerPush`），通过 `Serilog:Using` 引入 `Ray.BiliBiliTool.Push` 被 `ReadFrom.Configuration` 自动发现，与既有 Ray sink 走同一套配置绑定与热更新机制。
    - 飞书机器人：POST webhook，`{"msg_type":"text","content":{"text":...}}`
    - Bark：POST `{server}/{deviceKey}`，`{"title":"BiliTool","body":...}`
    - PushDeer：POST `{endpoint}/message/push`，`{"pushkey":...,"text":"BiliTool","desp":...,"type":"markdown"}`

- `src/Ray.BiliBiliTool.Web/appsettings.json`：`Serilog:Using` 追加 `Ray.BiliBiliTool.Push`；`Serilog:WriteTo` 追加 13/14/15（FeishuPush / BarkPush / PushDeerPush，带默认 server/endpoint）
- `src/Ray.BiliBiliTool.Web/Ray.BiliBiliTool.Web.csproj`：引用 `Ray.BiliBiliTool.Push`
- `src/Ray.BiliBiliTool.Console/appsettings.json`：`Serilog:Using` 追加 `Ray.BiliBiliTool.Push`；`Serilog:WriteTo` 追加 13/14/15 三个内置渠道（**Console 主机同样可用**）
- `src/Ray.BiliBiliTool.Console/Ray.BiliBiliTool.Console.csproj`：引用 `Ray.BiliBiliTool.Push`
- `Ray.BiliBiliTool.sln`：纳入 `Ray.BiliBiliTool.Push` 工程
- `src/Ray.BiliBiliTool.Web/Extensions/ServiceCollectionExtension.cs`：注册 `IPushSettingsWorkflow`
- `src/Ray.BiliBiliTool.Web/Components/Layout/NavMenu.razor`：新增「消息推送」导航入口
- `src/Ray.BiliBiliTool.Web/Program.cs`：**修复推送发送失败的上游缺陷**（见下）
- `docs/configuration.md`：补充「3.8 Notifications」关于 Web UI 的说明

## 修复的上游缺陷（重要）

**症状**：任何推送通道真正发消息时抛
`Could not load file or assembly 'Ray.Infrastructure, Version=0.0.26.0'`，消息永不送达。

**根因**：`Ray.Serilog.Sinks.*`（v0.1.6）编译时引用 `Ray.Infrastructure` **0.0.26**；而仓库出于 NU1902 安全通告（传递依赖 `SixLabors.ImageSharp` CVE，见 `common.props` 注释）**特意将 `Ray.Infrastructure` 钉在 0.1.2**。.NET Core 不做程序集版本统一，运行时无法加载被请求的 0.0.26 版本。该问题在主管道真实推送时同样触发（静默失败，错误仅进 SelfLog）。

**修复**：在 `Program.cs` 启动处注册 `AppDomain.AssemblyResolve` 桥接，将 `Ray.Infrastructure` 版本号 `< 0.1.0` 的加载请求定向到当前可用的 0.1.2 程序集（后向兼容小版本，Sink 仅用到通用 Helper）。不降级包版本、不重开安全通告。

## 验证

- 编译：`dotnet build src/Ray.BiliBiliTool.Web -c Debug` → 0 错误（仅有仓库既有的 MUD0002 / CS9124 等 warning）
- 端到端（本地运行 Web + 浏览器登录 admin 实测 + 本地捕获服务器确认真实 POST）：
  1. 页面渲染 13 通道、默认值读取（如 apiHost 默认 `https://api.telegram.org`、Bark server 默认 `https://api.day.app`）✓
  2. Telegram 配置保存 → 状态变「已配置」，SQLite 落库 `Serilog:WriteTo:3:Args:{botToken,chatId,apiHost}` ✓
  3. **新增渠道真实投递（指向本地捕获服务器 127.0.0.1:9999）**：
     - 飞书：点「发送测试消息」→ 捕获 `POST /feishu {"msg_type":"text","content":{"text":"…测试消息…通道：飞书机器人…"}}` ✓
     - Bark：保存+测试 → 捕获 `POST /testkey {"title":"BiliTool","body":"…通道：Bark…"}` ✓
     - PushDeer：保存+测试 → 捕获 `POST /message/push {"pushkey":…,"desp":"…通道：PushDeer…"}` ✓
  4. **热更新对自研 sink 生效**：保存飞书 webhook 后，主日志管道每 10s 自动批量 POST 到 `/feishu`（无需重启）✓
  5. 桥接修复后 Telegram 通道能真实发起 API 调用（本机网络不通 Telegram 属环境限制）✓
  6. **Console 主机接入验证**：编译（`-p:HUSKY=0`，0 错 0 警；`dotnet husky install` 为既有环境问题与本次无关）→ 启动无 sink 发现错误 → 将飞书 webhook 指向捕获服务器后启动 Console，捕获到 Console 日志的批量 `POST /console_feishu {"msg_type":"text",...}` ✓

## 测试计划

1. 启动 Web 面板，登录后台 → 任务配置 → 消息推送
2. 展开任意通道，填全必填项 → 保存 → 状态变「已配置」
3. 点「发送测试消息」→ 到目标应用确认收到（新渠道：飞书群机器人 / iOS Bark / PushDeer）
4. 清空某必填项 → 保存/测试应提示「请先填写必填项」
5. 重启应用确认配置仍在（持久化在 SQLite `bili_appsettings`）

> 说明：飞书 / Bark / PushDeer 三个内置渠道通过共享类库 `Ray.BiliBiliTool.Push` 在 Web 与 Console 双主机同时可用；Web 端可经「消息推送」页面可视化配置与测试，Console 端通过 `appsettings.json` 的 `Serilog:WriteTo:13/14/15` 配置。

## 自定义消息标题（Notify:Title）

**检查结论**：全部 13 个推送通道原本都无可配置的消息标题——10 个 Ray sink（Telegram / 钉钉 / 企业微信 / Server酱 / CoolPush / PushPlus / Teams / 自定义API / Gotify / 企业微信应用）的扩展方法参数经反射核验均无 `title`；自研 Bark/PushDeer 在源码里硬编码 `title="BiliTool"`，飞书为 `text` 类型无标题。

**最小改动方案**：所有通道共享同一条消息管道（`SerilogNotificationAdapter` 合成 `[NOTIFICATION] {Title}: {Body}` 后推进 Serilog）。因此在唯一出口注入一个可选的全局标题 `Notify:Title`——设置后每条推送文本统一以 `[<title>]` 开头，所有通道无需改动各自 sink 即全部带上自定义标题；留空则行为完全不变（无需 fork 外部二进制 sink 包）。

**改动文件**（commit `7cbef00`）：
- `src/Ray.BiliBiliTool.Infrastructure/Notifications/SerilogNotificationAdapter.cs`：构造函数注入 `IConfiguration`，读取 `Notify:Title`，在 `SendAsync` 与 `SendSummaryAsync` 两条路径统一加 `[<title>] ` 前缀。
- `src/Ray.BiliBiliTool.Web/appsettings.json` 与 `src/Ray.BiliBiliTool.Console/appsettings.json`：新增顶层 `"Notify": { "Title": "" }`（留空为默认）。
- `src/Ray.BiliBiliTool.Web/Components/Pages/Configs/PushConfig.razor` 与 `Services/Pages/Configs/PushSettingsWorkflow.cs`（commit `1a7def4`）：在「消息推送」页顶部新增「全局消息标题」卡片，经 SQLite 覆盖键 `Notify:Title` 读写并触发配置重载（`ReadTitle()` / `SaveTitle()`），留空则删除覆盖键回退默认；通道「发送测试消息」的测试文本同样带上 `[<title>]` 前缀（commit 末尾追加）。

**验证**（反射控制台 + 独立 harness，确定性核验）：
- 10 个 Ray sink 扩展方法签名均无 `title` 参数（botToken/chatId/webHookUrl/token/scKey/host/corpId/api/bodyJsonTemplate…）。✓
- 设置 `Notify:Title="我的BiliTool提醒"` 后：`SendAsync` → `[我的BiliTool提醒] [NOTIFICATION] 每日任务: 签到完成`；`SendSummaryAsync` → `[我的BiliTool提醒] [NOTIFICATION] Daily\n• Coin: 1`。✓
- 留空时：`[NOTIFICATION] 每日任务: 签到完成`（无前缀，行为不变）。✓
- 双主机编译 0 错误 0 警告。✓
- **测试消息带标题**：设 `Notify:Title` 后，Web 面板「发送测试消息」的载荷也带 `[<title>]` 前缀（本地捕获服务器实测 `POST /feishu_title {"msg_type":"text","content":{"text":"[测试标题X] [BiliTool 推送测试] …"}}`）。✓
