# Clipify 现代化与跨平台重构方案

> 状态：已确认，供后续实现使用  
> 日期：2026-07-26  
> 目标框架：.NET 10  
> 目标平台：Windows、macOS、Linux  
> 产品入口：Blazor Hybrid GUI、CLI、MCP Server

## 1. 文档目的

本文档将现阶段已经确认的技术决策整理为可执行的工程方案，作为后续使用 Cursor 实施重构时的主要依据。

本次工作不是在现有代码上继续堆功能，而是在保留 Clipify 核心特色——Blazor Hybrid——的前提下，重建跨平台桌面宿主、FFmpeg 基础设施和异步媒体任务系统，并提供 CLI 与 MCP 两个自动化入口，使相同能力可以被脚本、CI 和 AI Agent 调用。

实施过程中应优先保证：

1. 每个阶段都可以独立构建、测试和回滚。
2. UI、业务流程、FFmpeg 和桌面平台能力之间保持明确边界。
3. 不把 PhotinoX、Blazor Blueprint 或 FFmpeg.NET 类型泄漏到核心业务层。
4. 在新版本达到功能对等之前，保留 WinForms 版本作为行为参考。
5. GUI、CLI、MCP 只做输入输出适配，不重复实现视频处理逻辑。

## 2. 已确认的技术决策

### 2.1 保留 Blazor Hybrid

Clipify 继续以 Blazor Hybrid 作为核心技术特色。Razor 组件直接运行在本地 .NET 进程中，通过内嵌 WebView 渲染，不改成 Blazor WebAssembly，也不使用本地 Blazor Server 作为主要桌面架构。

### 2.2 使用 PhotinoX

跨平台桌面宿主采用 `PhotinoX.Blazor`：

- Windows：WebView2
- macOS：WKWebView
- Linux：WebKitGTK 4.1

暂时不 fork PhotinoX。所有 PhotinoX 类型必须限制在桌面宿主项目内，应用层通过自有接口访问文件选择、窗口、系统 Shell 和拖放等能力。

如果未来出现以下情况，再考虑维护最小化 fork：

- 上游缺少必须的扩展点；
- 存在阻塞发布的缺陷且无法在外部适配；
- .NET 新版本兼容修复长时间未发布；
- 必须修改底层资源协议或原生 WebView 行为。

### 2.3 使用 Blazor Blueprint

组件库采用 `BlazorBlueprint.Components`，使用其 shadcn/ui 风格的设计语言、组件和主题系统。

约束：

- 不再同时引入 Ant Design、Flowbite、daisyUI 等第二套主组件系统。
- 图标优先使用 Blazor Blueprint 的 Lucide 图标包。
- 自定义页面布局继续使用 Tailwind CSS。
- 使用 Blazor Blueprint 自带的预编译组件 CSS，并为 Clipify 自定义样式单独构建 Tailwind CSS。
- 主题通过 CSS Variables 和 OKLCH 色彩定义实现，避免在 Razor 组件里散落固定颜色。

截至本文日期，Blazor Blueprint 当前稳定版本为 `3.14.1`，支持 .NET 8 及以上；实际实施时使用当时的最新稳定版，并通过中央包管理锁定版本。

### 2.4 升级到 .NET 10

所有活动项目统一升级到 `net10.0`。桌面宿主不使用 Windows 专属 TFM。

仓库增加：

- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`

统一配置：

- Nullable
- ImplicitUsings
- TreatWarningsAsErrors（建议先对新项目启用）
- 分析器级别
- 语言版本
- 可重复构建
- 中央 NuGet 版本管理

### 2.5 精简依赖

新架构遵循“先使用 .NET BCL 和明确的小型依赖”的原则。

- 移除 `xFFmpeg.NET`，由 Clipify 自己管理 FFmpeg CLI 进程。
- 移除 Ant Design、Flowbite 和旧版 Font Awesome 前端依赖。
- 不默认迁移 MediatR。当前项目主要用它转发文件对话框事件，新架构直接使用明确的应用服务和状态流即可。
- 如果后续确实出现大量独立 Command/Handler，再通过 ADR 评估是否重新引入 MediatR。
- 所有版本通过 `Directory.Packages.props` 锁定，升级由独立 PR 完成。

### 2.6 删除 MAUI

`Clipify.Maui` 是不可用的半成品，不作为迁移来源，也不保留在活动解决方案中。

执行删除前创建一个 Git tag，例如：

```text
archive/maui-final
```

之后从仓库主分支和解决方案删除 `Clipify.Maui`。历史代码仍可通过 Git 访问，无需在仓库中保留一份长期失效的 `legacy` 副本。

### 2.7 WinForms 进入过渡维护

`Clipify.Forms` 在新版本达到功能对等之前保留，只处理阻塞迁移或严重缺陷，不再增加新功能。

PhotinoX 版本完成验收后：

1. 创建 `archive/winforms-final` Tag；
2. 从活动解决方案移除 WinForms；
3. 更新 README，仅描述新的跨平台版本；
4. 是否从主分支删除 WinForms 源码，以最终迁移 PR 的决定为准。

### 2.8 CLI 与 MCP 是一等入口

除 GUI 外，Clipify 同时提供：

- `clipify`：面向用户、脚本和 CI 的命令行程序；
- `clipify-mcp`：面向 Claude Code 等 Agent 的本地 MCP stdio Server。

三个入口共享：

- 相同的 Application Use Cases；
- 相同的异步任务系统；
- 相同的 FFmpeg/ffprobe 实现；
- 相同的 SQLite 任务历史；
- 相同的校验、错误代码和输出安全规则。

CLI/MCP 不引用 `Clipify.UI`，GUI 不通过启动 CLI 子进程来调用功能。

## 3. 当前代码的主要问题

### 3.1 UI 与 FFmpeg 强耦合

当前 Razor 页面直接生成 FFmpeg 字符串并执行：

- `Clipify.Forms/Pages/VideoSplit.razor.cs`
- `Clipify.Forms/Pages/ExtractAudio.razor.cs`
- `Clipify.Forms/Components/VideoExportDialog.razor.cs`

结果包括：

- UI 同时承担参数验证、命令生成、执行和状态展示；
- 相同命令生成逻辑在 Forms 和 Core 中重复；
- 进度事件直接绑定 Razor 组件；
- `async void` 事件处理难以传播异常；
- 页面关闭、导航和进程生命周期没有统一管理。

### 3.2 Core 不是纯核心层

`Clipify.Core` 直接依赖 `xFFmpeg.NET`，并暴露 `FFmpeg.NET.MetaData`、`Engine` 等基础设施类型。

`VideoProcessor` 还依赖 `IMessageService`，导致应用逻辑负责弹出 UI 消息。核心层不应知道 Toast、MessageBox 或 Dialog。

### 3.3 当前命令生成与执行不一致

`VideoProcessor.SplitVideoAsync` 生成了 FFmpeg 参数，但实际调用 `ConvertAsync` 时没有使用生成的参数。`progressCallback` 也没有被消费。

这意味着当前 Core 中的部分抽象只是表面存在，不能作为新架构直接迁移。

### 3.4 任务没有独立生命周期

当前导出操作的生命周期属于弹窗组件。应用无法可靠支持：

- 多任务排队；
- 任务历史；
- 取消与强制清理；
- 应用异常退出后的任务状态修复；
- 后台继续处理；
- 失败重试；
- 统一日志和产物管理。

## 4. 目标解决方案结构

建议将活动代码移动到 `src`，测试移动到 `tests`：

```text
Clipify.sln
global.json
Directory.Build.props
Directory.Packages.props

src/
  Clipify.Domain/
  Clipify.Application/
  Clipify.FFmpeg/
  Clipify.Persistence/
  Clipify.Hosting/
  Clipify.UI/
  Clipify.Desktop/
  Clipify.Cli/
  Clipify.Mcp/

tests/
  Clipify.Domain.Tests/
  Clipify.Application.Tests/
  Clipify.FFmpeg.Tests/
  Clipify.Persistence.Tests/
  Clipify.Cli.Tests/
  Clipify.Mcp.Tests/
  Clipify.UI.Tests/
```

### 4.1 Clipify.Domain

纯领域模型，不引用 UI、PhotinoX、数据库或 FFmpeg 包。

主要内容：

- `MediaJobId`
- `MediaWorkflowId`
- `MediaJobState`
- `MediaJobDefinition`
- `MediaJobSnapshot`
- `MediaJobProgress`
- `MediaArtifact`
- `MediaInfo`
- `MediaStreamInfo`
- `TimeRange`
- `OutputConflictPolicy`
- 领域校验和状态转换规则

### 4.2 Clipify.Application

用例、端口接口和异步任务编排。

主要内容：

- 创建裁剪任务；
- 创建音频提取任务；
- 查询媒体信息；
- 生成缩略图；
- 任务排队、查询、取消和重试；
- 任务处理器；
- 应用结果和结构化错误。

该项目只引用 `Clipify.Domain`。

### 4.3 Clipify.FFmpeg

FFmpeg/FFprobe 基础设施实现。

主要内容：

- FFmpeg 二进制发现和版本验证；
- 强类型命令构建；
- 进程启动、输出读取、取消和进程树清理；
- `-progress` 协议解析；
- ffprobe JSON 解析；
- 裁剪、音频提取、转码和缩略图处理器；
- 输出临时文件和最终产物提交。

该项目实现 `Clipify.Application` 中定义的接口。

### 4.4 Clipify.Persistence

SQLite 持久化实现。

主要内容：

- 任务定义和状态；
- 任务历史；
- 输出产物；
- 最近使用的设置；
- 应用异常退出后的状态修复。

首版可以使用 `Microsoft.Data.Sqlite` 和显式 SQL，避免为了少量表结构引入重量级 ORM。若实现团队更熟悉 EF Core，也可以使用 EF Core SQLite，但不能让数据库实体泄漏到 Domain。

### 4.5 Clipify.UI

Razor Class Library，包含全部共享 UI：

- 主布局和导航；
- 媒体文件选择区；
- 视频裁剪页；
- 音频提取页；
- 任务中心；
- 任务详情和日志；
- 设置页；
- 主题；
- Blazor Blueprint Providers。

该项目引用 `Clipify.Application`，不能引用 PhotinoX 或 `System.Diagnostics.Process`。

### 4.6 Clipify.Hosting

三个入口共享的 Generic Host 装配层：

- 注册 Application；
- 注册 Job Store、Queue 和 Worker；
- 注册 FFmpeg 与 ffprobe；
- 注册 SQLite 和日志；
- 绑定应用配置；
- 提供入口一致的启动、停止和诊断检查。

该项目是 Composition Root 的复用层，不包含 UI、命令解析或 MCP Tool。

### 4.7 Clipify.Desktop

唯一可执行桌面项目，负责：

- PhotinoX 启动和窗口配置；
- Generic Host 和依赖注入；
- 启动/停止后台任务 Worker；
- 原生文件与目录选择；
- 文件拖放；
- 打开文件、目录和外部链接；
- 本地媒体资源映射；
- 单实例；
- 应用数据目录；
- 平台运行时依赖检查；
- 日志初始化。

PhotinoX 类型只能出现在本项目。

### 4.8 Clipify.Cli

跨平台控制台程序，输出名称为 `clipify`。

负责：

- 使用 System.CommandLine 定义命令、参数和帮助；
- 将命令转换为 Application Request；
- 显示人类可读进度；
- 为自动化提供稳定的 JSON/JSON Lines 输出；
- 将应用错误映射为稳定 Exit Code。

CLI 不自行拼接 FFmpeg 命令，也不包含媒体业务规则。

### 4.9 Clipify.Mcp

本地 MCP stdio Server，输出名称为 `clipify-mcp`。

负责：

- 使用官方 `ModelContextProtocol` C# SDK；
- 将 MCP Tools 映射到 Application Use Cases；
- 提供面向 Agent 的 JSON Schema、描述和风险注解；
- 对文件系统访问实施独立于 MCP Roots 的强制路径限制；
- 将日志写入 stderr，保持 stdout 只承载 MCP JSON-RPC。

首版不提供远程 HTTP MCP 服务。

## 5. 依赖方向

```text
Clipify.Desktop ──┐
Clipify.Cli ──────┼── Clipify.Hosting ─┬── Clipify.Persistence
Clipify.Mcp ──────┘                    └── Clipify.FFmpeg
       │                                       │
       └── Clipify.Application ────────────────┘
                    │
              Clipify.Domain

Clipify.Desktop ──> Clipify.UI ──> Clipify.Application
```

禁止出现：

- Domain 引用 Application 或 Infrastructure；
- Application 引用 PhotinoX、SQLite、Blazor Blueprint；
- UI 直接启动 FFmpeg；
- FFmpeg 层弹出 Toast/Dialog；
- Razor 组件直接访问数据库；
- Desktop 类型泄漏到共享 Razor 组件。
- CLI 启动 GUI 完成任务；
- MCP 启动 CLI 并解析其文本输出；
- 三个入口各自实现一套业务规则。

## 6. 异步媒体任务系统

### 6.1 设计目标

任务系统用于承载所有耗时媒体操作：

- 媒体探测；
- 视频裁剪；
- 音频提取；
- 格式转换；
- 缩略图生成；
- 后续可能增加的合并、压缩和批处理。

系统是单机、本地进程架构，不引入 Hangfire、RabbitMQ、Redis 或分布式调度。

GUI、CLI、MCP 可能同时运行，因此“单机”不等于“永远只有一个进程”。任务存储必须支持多个 Clipify 入口安全地观察和竞争任务，但不把它扩展成网络分布式系统。

### 6.2 核心接口

接口命名可在实现时调整，但职责必须保留：

```csharp
public interface IMediaJobService
{
    ValueTask<MediaJobId> EnqueueAsync(
        MediaJobDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask RequestCancelAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    ValueTask<MediaJobId> RetryAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    ValueTask<MediaJobSnapshot?> GetAsync(
        MediaJobId jobId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<MediaJobChange> WatchAsync(
        CancellationToken cancellationToken = default);
}
```

内部接口：

```text
IMediaJobStore
IMediaJobQueue
IMediaJobHandler<TDefinition>
IMediaJobExecutor
IMediaArtifactStore
IJobCancellationRegistry
```

所有入口使用相同接口。GUI、CLI、MCP 不允许绕过 `IMediaJobService` 直接调用 Handler。

### 6.3 状态机

首版状态：

```text
Queued
  ├──> Running ──> Succeeded
  │       ├──────> Failed
  │       ├──────> Canceling ──> Canceled
  │       └──────> Interrupted
  └──────────────> Canceled
```

规则：

- 任务必须先持久化，再进入队列；
- 终态不可直接返回运行态；
- 取消是请求，不假设进程能够瞬间退出；
- 应用启动时发现的 `Running`/`Canceling` 任务标记为 `Interrupted`；
- 首版不支持暂停；
- 首版不自动继续未完成的 FFmpeg 输出；
- 重试通过复制原任务定义创建新任务，并记录 `RetryOfJobId`。

### 6.4 队列与持久化

推荐实现：

- SQLite 是任务状态的事实来源；
- `System.Threading.Channels` 只作为 Worker 的有界唤醒/调度机制；
- `MediaJobWorker : BackgroundService` 消费任务；
- Worker 启动时从 SQLite 查找待运行任务；
- Worker 使用短周期 `PeriodicTimer` 扫描数据库，以发现由其他 Clipify 进程提交的任务；
- 默认并发数为 1，设置页允许调整为 1～2；
- 同优先级按创建时间 FIFO；
- 队列必须有界，避免批量导入时无限占用内存。

不要只把任务对象放进内存 Channel，否则应用退出后排队任务会丢失。

### 6.5 多入口与任务租约

为了避免 GUI、CLI 和 MCP 同时执行同一任务，Job Store 增加：

```text
LeaseOwner
LeaseAcquiredAt
LeaseExpiresAt
HeartbeatAt
CancelRequestedAt
```

规则：

- Worker 必须通过 SQLite 事务原子 Claim 任务；
- Claim 同时检查全局并发限制；
- 运行期间定期续租；
- 每个运行任务持有一个独占 Job Lock 文件，作为进程仍然存活的第二重证据；
- 其他进程只能观察任务或设置 `CancelRequestedAt`；
- 实际运行任务的 Worker 轮询取消请求并触发本地 CancellationToken；
- 发现过期租约且独占 Lock 已释放时，将任务标记为 `Interrupted`；
- 不自动重新执行 Interrupted 的 FFmpeg 任务，避免重复覆盖输出；
- SQLite Busy/Locked 使用有限退避重试，不能无限阻塞 UI 或 MCP Tool。

首版不增加常驻 Daemon。GUI、CLI、MCP 都可以在自身进程中启动 Worker。未来若需要“关闭全部入口后任务仍继续运行”，可以增加 `Clipify.Daemon`，但必须复用相同的 Job Store 和 Application Contract。

### 6.6 进度模型

进度不能只有一个 `double`。建议至少包含：

```csharp
public sealed record MediaJobProgress(
    double? Fraction,
    TimeSpan? ProcessedDuration,
    TimeSpan? TotalDuration,
    double? Speed,
    TimeSpan? EstimatedRemaining,
    string Stage,
    string? Message,
    DateTimeOffset UpdatedAt);
```

进度策略：

- UI 更新可以实时通过内存事件流发送；
- SQLite 进度写入节流到约 500ms～1s；
- 完成、失败、取消等状态必须立即持久化；
- 完整 FFmpeg 日志写滚动日志文件；
- SQLite 只保存摘要、错误信息和日志文件位置。

### 6.7 工作流扩展

首版不要实现通用 DAG 工作流引擎。

一个高层任务处理器可以包含多个内部阶段，例如：

```text
Probe
  -> Validate
  -> PrepareOutput
  -> RunFFmpeg
  -> VerifyOutput
  -> CommitArtifact
```

领域模型预留：

- `WorkflowId`
- `ParentJobId`
- `Stage`

以后出现真正需要并行依赖的场景，再扩展成 DAG。

### 6.8 应用退出

关闭应用时：

1. 停止接受新任务；
2. 请求取消运行任务；
3. 等待有限的优雅退出时间；
4. 超时后终止 FFmpeg 进程树；
5. 将未正常结束的任务标记为 `Interrupted`；
6. 刷新状态和日志后退出。

不能让 Photino 窗口关闭后残留 FFmpeg 或 .NET 后台进程。

CLI 的普通媒体命令默认等待自己提交的任务完成。首版不提供无法保证后台 Worker 存活的 `--detach`。MCP Server 可以返回 JobId 后继续在 stdio 会话期间运行 Worker。

## 7. FFmpeg 层重新设计

### 7.1 移除 xFFmpeg.NET

目标架构不继续使用 `xFFmpeg.NET`，原因：

- 当前库类型已经泄漏进 Core 和 UI；
- 现有命令生成与实际执行不一致；
- Clipify 需要精确控制参数、进度、取消和子进程；
- FFmpeg CLI 本身已经提供稳定的机器可读进度协议。

建议基于 `System.Diagnostics.Process` 实现小而专用的执行器，不通过 Shell 启动。

### 7.2 强类型参数

禁止继续拼接完整命令行字符串：

```csharp
var arguments = $"-i \"{input}\" ...";
```

应构建参数列表并使用：

```csharp
processStartInfo.ArgumentList.Add(argument);
```

这样可以正确处理：

- 空格；
- 中文路径；
- 引号；
- 特殊字符；
- 不同操作系统的参数转义。

### 7.3 主要接口

```text
IFFmpegLocator
IFFmpegVersionProbe
IFFmpegCommandBuilder<TDefinition>
IFFmpegProcessRunner
IFFmpegProgressParser
IFFprobeClient
IOutputCommitter
```

`IFFmpegProcessRunner` 返回 Clipify 自己的结果模型，不返回第三方库类型。

### 7.4 进度协议

执行时使用类似参数：

```text
-nostdin
-hide_banner
-nostats
-stats_period 0.5
-progress pipe:1
```

标准输出解析 `key=value`：

- `out_time_us` / `out_time_ms`
- `speed`
- `fps`
- `frame`
- `total_size`
- `progress=continue|end`

标准错误用于人类可读日志。不要从普通 stderr 文本正则猜测核心进度。

### 7.5 ffprobe

媒体元数据统一使用 ffprobe JSON：

```text
-v error
-show_format
-show_streams
-of json
```

反序列化为 Clipify 自己的 `MediaInfo` 和 `MediaStreamInfo`，禁止向上层暴露 ffprobe 原始 DTO。

### 7.6 取消和进程清理

取消任务时：

1. 触发任务 `CancellationToken`；
2. 尝试正常结束；
3. 超时后调用 `Kill(entireProcessTree: true)`；
4. 等待进程退出并排空输出流；
5. 清理未提交的临时产物；
6. 将任务状态更新为 `Canceled` 或 `Failed`。

### 7.7 输出安全

所有媒体任务必须：

- 在最终输出文件相同目录创建带 JobId 的临时输出；
- 成功并验证后再移动为最终文件；
- 明确处理覆盖、重命名、跳过三种冲突策略；
- 取消或失败时不留下伪装成完整结果的文件；
- 不能默认无条件加入 `-y`。

建议临时命名：

```text
.<filename>.clipify-<jobId>.partial.<extension>
```

### 7.8 FFmpeg 分发

按 RID 管理 FFmpeg/ffprobe：

```text
win-x64
win-arm64（后续）
linux-x64
linux-arm64（后续）
osx-x64
osx-arm64
```

启动时检查：

- 二进制是否存在；
- 是否可执行；
- FFmpeg/ffprobe 版本；
- 目标架构是否匹配。

仓库和发布包保留第三方许可证与版本信息。FFmpeg 具体分发方式应根据所选构建的 LGPL/GPL 配置复核。

## 8. UI 与 Blazor Blueprint

### 8.1 基础接入

在 `Clipify.UI` 中：

- 注册 `AddBlazorBlueprintComponents()`；
- `_Imports.razor` 引入组件和 Lucide 图标；
- 主布局加入：
  - `BbPortalHost`
  - `BbToastProvider`
  - `BbDialogProvider`
- 加载：
  - Clipify Theme CSS
  - Blazor Blueprint 预编译 CSS
  - Clipify 自定义 Tailwind CSS

Tailwind 使用 v4，不沿用当前 Gulp + Tailwind 3 构建链。优先使用 Tailwind 独立 CLI，并由 MSBuild 或明确的前端构建脚本调用。

### 8.2 组件映射

建议：

| Clipify 场景 | Blazor Blueprint |
|---|---|
| 主操作按钮 | Button |
| 参数输入 | Input、Numeric Input、Select、Switch |
| 时间范围 | Time Picker 或自定义时间输入组合 |
| 导出确认 | Dialog / Alert Dialog |
| 轻量反馈 | Toast |
| 任务进度 | Progress |
| 任务列表 | Data Table |
| 任务详情 | Sheet / Drawer |
| 状态 | Badge |
| 右键操作 | Context Menu |
| 快速命令 | Command |
| 空列表 | Empty |
| 加载占位 | Skeleton |

### 8.3 大文件选择

不要使用普通浏览器文件上传流程读取本地视频内容。

Blazor Blueprint 的 File Upload 适合 Web 上传，但 Clipify 的视频可能很大。桌面版必须通过 `IFilePicker` 和原生拖放获得本地文件路径，由 FFmpeg 直接读取文件。

可以复用 Blueprint 的视觉语言实现拖放区，但不能把视频复制为 `IBrowserFile` 后再传入 .NET。

### 8.4 UI 状态

Razor 组件只能：

- 提交应用命令；
- 读取 Job Snapshot；
- 订阅 Job Change；
- 请求取消或重试；
- 展示结构化错误。

Razor 组件不能：

- 订阅 FFmpeg 进程事件；
- 持有 FFmpeg Engine；
- 直接修改数据库；
- 自己维护真实任务状态。

任务中心是任务状态的主要 UI。导出弹窗只能显示任务摘要，关闭弹窗不能取消任务。

## 9. 桌面平台抽象

在 Application 或专门的共享 Abstractions 中定义：

```text
IFilePicker
IFolderPicker
IFileDropService
IShellService
IClipboardService
IWindowService
INotificationService
IAppPathProvider
IMediaResourceProvider
```

原则：

- PhotinoX 类型不离开 `Clipify.Desktop`；
- 文件选择返回真实本地路径；
- 外部 URL 使用系统浏览器打开；
- 用户媒体不暴露为任意可导航的 WebView 文件 URL；
- 所有 WebView 到本地能力的入口都要验证参数和来源。

## 10. CLI 设计

### 10.1 命令结构

使用官方 `System.CommandLine`，初始命令建议：

```text
clipify doctor
clipify probe <input>
clipify trim <input> --start <time> --end <time> --output <path>
clipify extract-audio <input> --output <path> [--format <format>]
clipify thumbnail <input> --output <path> [--at <time>]

clipify jobs list
clipify jobs get <job-id>
clipify jobs wait <job-id>
clipify jobs cancel <job-id>
clipify jobs retry <job-id>
```

后续能力成熟后再增加：

```text
clipify convert
clipify merge
clipify batch
```

CLI 命令必须调用 Application Use Case，不能自己拼 FFmpeg 参数。

### 10.2 输出模式

支持三种输出：

```text
默认          面向人类的文本、进度条和摘要
--json        stdout 只输出最终 JSON
--jsonl       stdout 输出结构化事件流
```

规则：

- stdout 是结果通道；
- stderr 是诊断和人类可读进度通道；
- `--json` 下不能混入日志、Banner 或进度文本；
- JSON 字段使用稳定的 snake_case；
- 时间使用 ISO 8601，时长同时提供机器可读毫秒值；
- JobId、状态和错误代码必须稳定；
- 完整 FFmpeg stderr 不直接写入 JSON，返回截断摘要和日志路径。

### 10.3 Exit Code

首版固定：

| Code | 含义 |
|---:|---|
| 0 | 成功 |
| 1 | 未分类内部错误 |
| 2 | 参数或校验错误 |
| 3 | 文件不存在或无权限 |
| 4 | FFmpeg/ffprobe 不可用 |
| 5 | 媒体任务失败 |
| 6 | 任务被取消 |
| 7 | 任务被中断 |

不得根据错误消息文本推断 Exit Code。

### 10.4 CLI 行为

- 普通媒体命令提交 Job 后等待完成；
- `Ctrl+C` 第一次请求取消任务并等待清理；
- 第二次 `Ctrl+C` 可以强制结束，但仍应尽量标记 Interrupted；
- `jobs` 命令操作共享 SQLite Job Store；
- `doctor` 检查 FFmpeg、ffprobe、SQLite、运行目录和平台依赖；
- 默认不覆盖输出文件；
- CLI 的功能和 GUI 保持相同，不增加“仅 CLI 可用的原始 FFmpeg 参数”逃生口。

## 11. MCP Server 设计

### 11.1 SDK 与传输

使用官方 `ModelContextProtocol` C# SDK：

```text
ModelContextProtocol
Microsoft.Extensions.Hosting
```

首版只支持 stdio：

- 适合 Claude Code、IDE 和本地 Agent 按需启动；
- stdout 专用于 MCP JSON-RPC；
- 所有日志写 stderr；
- 不监听 TCP 端口；
- 不实现鉴权和远程多租户；
- 不依赖实验性的 MCP 长任务扩展。

远程 Streamable HTTP 必须作为独立安全设计处理，不能简单给 stdio Server 增加一个监听地址。

### 11.2 MCP Tools

首版工具：

| Tool | 类型 | 说明 |
|---|---|---|
| `get_capabilities` | 只读 | 返回版本、格式、编解码能力和路径策略 |
| `probe_media` | 只读 | 返回媒体、容器和流信息 |
| `trim_video` | 写入 | 创建视频裁剪任务 |
| `extract_audio` | 写入 | 创建音频提取任务 |
| `generate_thumbnail` | 写入 | 创建缩略图任务 |
| `list_jobs` | 只读 | 查询任务摘要 |
| `get_job` | 只读 | 查询一个任务和产物 |
| `wait_job` | 只读 | 有限时间等待状态变化 |
| `cancel_job` | 写入 | 请求取消任务 |
| `retry_job` | 写入 | 基于失败/中断任务创建新任务 |

工具名称使用稳定的 snake_case。新增工具不能改变现有 Tool 的输入/输出语义。

`list_jobs` 必须支持状态过滤、游标或分页，默认返回不超过 20 项并设置合理上限。`get_job` 只返回日志摘要和有限长度的尾部内容，完整日志通过受限本地路径定位，避免消耗大量 Agent Context。

不要提供：

- `run_ffmpeg(arguments)`；
- 任意 Shell 命令；
- 任意 URL 下载；
- 删除任意本地文件；
- 将完整视频内容作为 MCP Content 返回。

### 11.3 长任务语义

媒体处理 Tool 默认快速返回：

```json
{
  "job_id": "01...",
  "state": "queued",
  "operation": "trim_video",
  "created_at": "2026-07-26T00:00:00Z"
}
```

Agent 后续调用：

```text
get_job(job_id)
wait_job(job_id, timeout_seconds)
```

`wait_job`：

- 单次等待设置较小上限，例如 60 秒；
- 超时不是任务失败，返回最新 Snapshot；
- 支持客户端取消；
- 不在一次响应中返回无限增长的日志；
- 任务完成后返回产物路径、大小、媒体摘要和结构化错误。

即使未来 MCP SDK 提供协议级长任务能力，也应先保留现有 Job API，避免绑定单一客户端或实验协议。

### 11.4 Tool Schema

Tool 参数使用明确类型和描述：

- 输入路径；
- 输出路径；
- 开始/结束时间；
- 输出格式；
- 冲突策略；
- 可选任务优先级。

时间接受清晰、无歧义的格式，例如：

```text
HH:MM:SS.fff
整数毫秒
```

响应统一包含：

```text
ok
job_id
state
result
error.code
error.message
warnings
```

MCP 层只做 Schema/DTO 到 Application Request 的映射，业务校验仍由 Application 执行。

### 11.5 Tool Annotations

为工具设置 MCP 风险提示：

- 查询工具：`readOnlyHint=true`；
- 创建新输出文件：`readOnlyHint=false`、`destructiveHint=false`；
- `cancel_job` 会终止处理并清理临时输出，应标记为破坏性；
- 允许覆盖输出时：按破坏性操作处理；
- 重复执行会产生新 Job 的工具不能标记为幂等；
- 本地、受限路径工具可设置 `openWorldHint=false`。

Tool Annotations 只是提示，不能代替服务端权限校验。

### 11.6 文件系统安全

本地 MCP Server 与 Agent 具有相同的操作系统权限，因此必须额外限制：

- 默认只允许访问 MCP Server 启动时的当前工作目录；
- 使用可重复的 `--allow-root <path>` 或 `CLIPIFY_ALLOWED_ROOTS` 增加目录；
- 输入和输出都必须位于允许目录；
- 将 MCP Roots 作为额外提示，但不能把它当作安全边界；
- 在访问前规范化绝对路径；
- 处理 Windows 大小写、UNC、符号链接、目录联接和 `..`；
- 默认禁止覆盖；
- 输出目录不存在时是否创建必须显式；
- 首版禁止网络 URL；
- 不读取 SSH、凭据、系统目录等无关文件；
- 错误响应避免泄露允许目录之外的路径信息。

允许目录校验必须有跨平台单元测试和安全回归测试。

### 11.7 Agent 集成

发布包提供 Claude Code 示例：

```json
{
  "mcpServers": {
    "clipify": {
      "type": "stdio",
      "command": "/absolute/path/to/clipify-mcp",
      "args": ["--allow-root", "/absolute/path/to/media-workspace"],
      "env": {}
    }
  }
}
```

实际配置格式应在发布时用当前 Claude Code 文档验证。不要在仓库提交用户机器的绝对路径。

同时提供通用 stdio MCP 配置说明，不把产品文档绑定到 Claude Code 单一客户端。

### 11.8 MCP Resources

首版以 Tools 为主。稳定后可以增加只读 Resources：

```text
clipify://capabilities
clipify://jobs/{jobId}
clipify://jobs/{jobId}/artifacts
```

不通过 Resource 暴露原始媒体文件内容。

## 12. 错误模型与日志

不要继续使用 `Task<bool>`、吞异常后返回 `null` 的方式表达失败。

建议：

```text
Result<T>
ClipifyError
ClipifyErrorCode
ValidationError
FfmpegProcessError
MediaProbeError
OutputConflictError
```

日志：

- 使用 `Microsoft.Extensions.Logging`；
- 桌面端输出滚动文件日志；
- 日志包含 `JobId`、操作类型、输入、输出和 FFmpeg ExitCode；
- UI 展示用户可理解的错误摘要；
- 完整命令参数和 stderr 保留在诊断日志中；
- 默认避免记录敏感目录之外的无关用户数据。

## 13. 测试策略

### 13.1 Domain

- 时间范围验证；
- 输出路径和冲突策略；
- Job 状态转换；
- 终态不可逆；
- 重试关联。

### 13.2 Application

- 入队先持久化；
- FIFO 和并发限制；
- 取消排队任务；
- 取消运行任务；
- Worker 异常不会停止后续任务；
- 启动时修复 Interrupted；
- 重试创建新任务；
- 状态和进度事件顺序。

### 13.3 FFmpeg

- 参数列表快照测试；
- 中文、空格、特殊字符路径；
- progress 协议解析；
- ffprobe JSON 解析；
- 非零 ExitCode；
- 取消与超时；
- 进程树清理；
- 临时输出提交和清理；
- 小型媒体文件端到端测试。

测试媒体应体积很小，并明确其许可证和来源。也可以在测试准备阶段使用 FFmpeg lavfi 生成。

### 13.4 Persistence

- SQLite Schema Migration；
- 并发状态更新；
- 应用重启后的队列恢复；
- Running 到 Interrupted 的修复；
- 历史和产物查询。

### 13.5 UI

使用 bUnit 覆盖：

- 表单校验；
- 创建任务；
- 任务进度和状态显示；
- 取消与重试；
- 空状态；
- 错误状态；
- Blazor Blueprint Provider 配置。

### 13.6 CLI

- 命令解析与帮助；
- 人类、JSON、JSONL 输出互不污染；
- stdout/stderr 分离；
- Exit Code；
- Ctrl+C 取消；
- 所有命令只调用 Application Contract。

### 13.7 MCP

- Tool Schema 快照；
- Tool Annotations；
- stdio stdout 无日志污染；
- 路径根限制、路径穿越、符号链接和目录联接；
- 长任务返回 JobId；
- wait 超时语义；
- Agent 取消 Tool Call；
- 错误响应大小限制；
- 与参考 MCP Client 的协议集成测试。

### 13.8 平台 Smoke Test

在 Windows、macOS、Linux 分别验证：

- 应用启动；
- WebView 加载；
- 文件/目录选择；
- 文件拖放；
- 视频预览；
- 启动和取消 FFmpeg；
- 打开输出目录；
- 应用关闭无残留进程；
- CLI 文本与 JSON 模式可运行；
- MCP stdio 握手、Tool Discovery 和一次完整 Job 流程；
- 深色/浅色主题；
- 中文输入法和高 DPI。

## 14. CI 与发布

GitHub Actions 构建矩阵：

```text
windows-latest
ubuntu-latest
macos-latest
```

每次 PR：

1. Restore；
2. Build；
3. Unit Tests；
4. FFmpeg Parser/Command Tests；
5. bUnit；
6. CLI Snapshot/Exit Code Tests；
7. MCP Schema/Security/Protocol Tests；
8. 格式和分析器检查。

发布阶段：

- Windows：先提供 self-contained ZIP，再增加安装包；
- macOS：`.app` + 签名/公证，后续提供 DMG；
- Linux：先提供 tarball，再评估 AppImage 或 Flatpak；
- 每个平台同时发布 `clipify` 和 `clipify-mcp`；
- 每个平台发布包包含对应 FFmpeg/ffprobe 或提供受控下载流程；
- 生成第三方许可证清单和校验值。

首版优先 RID：

```text
win-x64
linux-x64
osx-x64
osx-arm64
```

## 15. 分阶段实施计划

### 阶段 0：建立安全基线

- 确认工作树；
- 创建重构分支；
- 为当前版本创建 Tag；
- 记录当前 WinForms 的可用功能和已知缺陷；
- 保留截图和最小手工验收步骤。

验收：

- 当前 WinForms 构建结果被记录；
- 当前功能基线可复现；
- 没有在未记录的情况下删除代码。

### 阶段 1：解决方案与 .NET 10 基础

- 添加 `global.json`；
- 添加中央 Build/Package 配置；
- 创建新的 `src`/`tests` 项目；
- 升级活动代码到 .NET 10；
- 建立最小测试和 CI；
- 不在此阶段迁移 UI 功能。

验收：

- 新项目在 .NET 10 下构建；
- CI 三平台基础构建通过；
- 包版本集中管理。

### 阶段 2：删除 MAUI

- 创建 MAUI 归档 Tag；
- 从解决方案删除 MAUI；
- 删除 `Clipify.Maui`；
- 清理只服务于 MAUI 的依赖和文档。

验收：

- 解决方案不包含 MAUI；
- 仓库不要求安装 MAUI Workload；
- 新旧桌面项目仍可构建。

### 阶段 3：Domain、Application 与任务系统

- 建立 Job 模型和状态机；
- 实现 SQLite Job Store；
- 实现 Channel 唤醒和 BackgroundService Worker；
- 实现排队、取消、重试、历史和中断修复；
- 实现跨进程 Claim、Lease、Heartbeat 和 Job Lock；
- 使用 Fake Handler 完成全部任务系统测试。

验收：

- 不依赖 FFmpeg 也能完整验证任务系统；
- 应用重启后状态正确；
- 两个测试 Host 不会重复执行同一 Job；
- 并发限制可靠；
- 没有 `async void` 任务事件。

### 阶段 4：FFmpeg 基础设施

- 删除新代码对 xFFmpeg.NET 的依赖；
- 实现 locator、runner、progress parser 和 ffprobe；
- 实现裁剪、音频提取、缩略图；
- 接入任务处理器；
- 建立端到端媒体测试。

验收：

- 参数不通过 Shell；
- 进度来自 `-progress`；
- 取消会清理进程树；
- 失败不会留下正式输出；
- 中文路径测试通过。

### 阶段 5：共享 Hosting 与 CLI

- 创建 `Clipify.Hosting`；
- 统一注册任务、Persistence、FFmpeg 和日志；
- 创建 `Clipify.Cli`；
- 实现 probe、trim、extract-audio、thumbnail 和 jobs；
- 实现文本、JSON、JSONL 输出；
- 实现 Exit Code、Ctrl+C 和 doctor；
- 清理 `ClipifyConveter`，将有价值的行为迁移到 CLI。

验收：

- CLI 与 Application 共享校验和任务执行；
- JSON stdout 无日志污染；
- CLI 不包含 FFmpeg 命令拼接；
- Windows、macOS、Linux 行为一致；
- 两个 CLI 进程不会重复执行 Job。

### 阶段 6：MCP Server

- 创建 `Clipify.Mcp`；
- 接入官方 C# MCP SDK 和 stdio；
- 实现首版 Tools 和 Schema；
- 实现 JobId + get/wait 长任务语义；
- 实现 allow-root、路径规范化和安全测试；
- 添加 Claude Code 与通用 MCP 配置示例。

验收：

- stdout 只有 MCP JSON-RPC；
- 参考 MCP Client 可发现并调用所有 Tools；
- 不允许访问允许目录之外的输入或输出；
- 写入工具包含正确风险注解；
- 长任务不会让单次 Tool Call 无限等待；
- MCP 与 GUI/CLI 共享任务历史且不重复执行。

### 阶段 7：PhotinoX Shell

- 创建 `Clipify.Desktop`；
- 复用 `Clipify.Hosting`；
- 接入 `PhotinoX.Blazor`；
- 实现平台服务；
- 启动/停止 MediaJobWorker；
- 实现本地媒体预览资源策略。

验收：

- 同一个可执行项目能为四个首版 RID 发布；
- UI 和 Application 不引用 PhotinoX；
- 关闭应用无残留任务和进程。

### 阶段 8：Blazor Blueprint UI

- 创建 `Clipify.UI` RCL；
- 配置 Blueprint Providers 和 Tailwind v4；
- 实现布局、主题、文件选择、任务中心；
- 迁移裁剪和音频提取；
- 替换 Ant Design、Flowbite 和旧弹窗。

验收：

- 两项现有功能达到行为对等；
- 任务离开弹窗后仍继续执行；
- 任务中心可取消、重试和查看历史；
- 组件在三个 WebView 引擎下可用。

### 阶段 9：功能对等与 WinForms 归档

- 对照基线测试；
- 修复新版本功能差异；
- 创建 WinForms 归档 Tag；
- 从活动解决方案移除 WinForms；
- 清理重复服务和旧前端构建链；
- 删除已经由新 CLI 替代的 `ClipifyConveter`。

验收：

- 主解决方案只包含新架构；
- 不再依赖 WindowsForms；
- 不再依赖 xFFmpeg.NET、AntDesign、Flowbite；
- README 与真实构建方式一致。

### 阶段 10：发布完善

- 三平台安装和依赖检测；
- CLI/MCP PATH 安装方式；
- Claude Code 与通用 Agent 接入文档；
- macOS 签名/公证；
- Linux 包格式；
- 崩溃日志和诊断导出；
- 自动更新方案另行 ADR。

## 16. Cursor 实施规则

交给 Cursor 实现时，应附带以下约束：

1. 每次只实施一个阶段或一个清晰子阶段。
2. 修改前先阅读本文档及涉及的现有文件。
3. 每个阶段先写或更新测试，再迁移生产代码。
4. 不在同一个提交中同时完成项目移动、架构重写和 UI 重做。
5. 不直接复制现有重复代码到新项目。
6. 不为了“以后可能有用”引入通用工作流框架。
7. 不让 UI 直接调用 FFmpeg 或 SQLite。
8. 不把 PhotinoX 类型暴露给 UI/Application。
9. 不让 CLI/MCP 复制或绕过 Application 业务规则。
10. 不用完整字符串拼接 FFmpeg 命令。
11. 不用 `Task<bool>` 或空 catch 隐藏错误。
12. 不使用 `async void`，UI 事件入口除外；即使是 UI 入口也应立即委托给可等待方法。
13. 不在 MCP 中暴露任意 Shell、原始 FFmpeg 参数或无限制文件系统访问。
14. 每次新增 NuGet 包必须说明用途和替代方案。
15. 每个里程碑结束必须运行 Build、Tests，并更新本文档中的实际偏差。
16. 如果实现发现方案与平台现实冲突，先记录 ADR，不得静默改变架构。

建议 Cursor 每阶段输出：

- 修改摘要；
- 新增/删除项目；
- 关键设计决定；
- 执行过的命令；
- 测试结果；
- 已知问题；
- 下一阶段前置条件。

## 17. 明确的非目标

当前重构不包含：

- Android/iOS；
- MAUI 恢复；
- 自研跨平台 BlazorWebView；
- PhotinoX fork；
- 云端转码；
- 远程 HTTP MCP Server；
- 常驻 Clipify Daemon；
- 分布式任务队列；
- 通用 DAG 工作流平台；
- 多用户；
- 插件市场；
- 默认启用硬件编码；
- 完整非线性视频编辑时间线；
- 自动更新的最终选型；
- 允许 Agent 执行任意 Shell/FFmpeg 参数。

这些能力必须在基础架构稳定后单独评估。

## 18. 完成定义

本轮现代化完成需同时满足：

- 使用 .NET 10；
- Windows、macOS、Linux 均由 PhotinoX 承载同一个 Blazor UI；
- UI 使用 Blazor Blueprint；
- 三个平台均提供 `clipify` CLI；
- 三个平台均提供 `clipify-mcp` stdio Server；
- GUI、CLI、MCP 共享 Application、FFmpeg 和 Job Store；
- CLI JSON 输出和 Exit Code 稳定；
- MCP Tool Schema、路径限制和风险注解具有自动化测试；
- Claude Code 可以通过文档中的配置发现并调用 Clipify；
- MAUI 已删除；
- WinForms 已归档并退出活动解决方案；
- xFFmpeg.NET 已退出活动代码；
- FFmpeg 进度、取消和错误处理可靠；
- 所有媒体处理通过持久化异步任务系统执行；
- 应用崩溃或强制退出后任务状态可解释；
- 失败和取消不会留下伪完整文件；
- 核心功能有自动化测试；
- 三平台具有可验证的发布产物；
- README、构建说明和架构文档与实际代码一致。

## 19. 参考资料

- [Microsoft：Blazor Hosting Models / Blazor Hybrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0)
- [PhotinoX.Blazor NuGet](https://www.nuget.org/packages/PhotinoX.Blazor)
- [PhotinoX.Blazor GitHub](https://github.com/ivanvoyager/PhotinoX.Blazor)
- [Blazor Blueprint 安装文档](https://blazorblueprintui.com/docs/installation)
- [Blazor Blueprint 组件列表](https://blazorblueprintui.com/components)
- [BlazorBlueprint.Components NuGet](https://www.nuget.org/packages/BlazorBlueprint.Components)
- [.NET Hosted Services 与有界 Channel 队列](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
- [Microsoft：System.CommandLine](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [MCP 官方 C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK：stdio Server 入门](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)
- [MCP Security Best Practices](https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices)
- [Claude Code：MCP 配置](https://docs.anthropic.com/en/docs/claude-code/mcp)
- [FFmpeg `-progress` 官方文档](https://ffmpeg.org/ffmpeg.html)

## 20. 交给 Cursor 的首轮指令

建议不要让 Cursor 一次执行整份方案。第一轮可以直接提供以下指令：

```text
请先完整阅读 docs/modernization-plan.md、README.md、Clipify.sln，以及现有
Clipify.Core 和 Clipify.Forms 中与 FFmpeg、依赖注入、视频裁剪、音频提取有关的代码。

本轮只实施“阶段 0：建立安全基线”和“阶段 1：解决方案与 .NET 10 基础”。
不要删除 MAUI，不要迁移 UI，不要实现 FFmpeg/CLI/MCP，不要引入 PhotinoX。

要求：
1. 先报告当前工作树和构建基线。
2. 给出本轮准确的文件变更计划。
3. 增加 global.json、Directory.Build.props、Directory.Packages.props。
4. 建立 docs 中规划的新项目骨架，但只加入维持依赖方向所需的最小代码。
5. 增加最小测试项目和 CI。
6. 不把现有问题代码原样复制进新项目。
7. 完成后运行 restore、build、test，并报告所有警告和失败。
8. 如果实际环境与方案冲突，停止相关修改并说明，不要自行扩大范围。
```

阶段 1 验收后，再单独要求 Cursor 执行阶段 2。后续每一阶段都沿用同样的“小范围、可验证、完成后再继续”方式。
