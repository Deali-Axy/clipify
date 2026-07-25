# Clipify 现代化与跨平台重构方案

> 状态：已确认，供后续实现使用  
> 日期：2026-07-26  
> 目标框架：.NET 10  
> 目标平台：Windows、macOS、Linux 桌面端

## 1. 文档目的

本文档将现阶段已经确认的技术决策整理为可执行的工程方案，作为后续使用 Cursor 实施重构时的主要依据。

本次工作不是在现有代码上继续堆功能，而是在保留 Clipify 核心特色——Blazor Hybrid——的前提下，重建跨平台桌面宿主、FFmpeg 基础设施和异步媒体任务系统。

实施过程中应优先保证：

1. 每个阶段都可以独立构建、测试和回滚。
2. UI、业务流程、FFmpeg 和桌面平台能力之间保持明确边界。
3. 不把 PhotinoX、Blazor Blueprint 或 FFmpeg.NET 类型泄漏到核心业务层。
4. 在新版本达到功能对等之前，保留 WinForms 版本作为行为参考。

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
  Clipify.UI/
  Clipify.Desktop/

tests/
  Clipify.Domain.Tests/
  Clipify.Application.Tests/
  Clipify.FFmpeg.Tests/
  Clipify.Persistence.Tests/
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

### 4.6 Clipify.Desktop

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

## 5. 依赖方向

```text
Clipify.Desktop ───────────────┐
      │                        │
      ├── Clipify.UI           │
      ├── Clipify.Persistence  │
      └── Clipify.FFmpeg       │
                │              │
                └── Clipify.Application
                           │
                     Clipify.Domain
```

禁止出现：

- Domain 引用 Application 或 Infrastructure；
- Application 引用 PhotinoX、SQLite、Blazor Blueprint；
- UI 直接启动 FFmpeg；
- FFmpeg 层弹出 Toast/Dialog；
- Razor 组件直接访问数据库；
- Desktop 类型泄漏到共享 Razor 组件。

## 6. 异步媒体任务系统

### 6.1 设计目标

任务系统用于承载所有耗时媒体操作：

- 媒体探测；
- 视频裁剪；
- 音频提取；
- 格式转换；
- 缩略图生成；
- 后续可能增加的合并、压缩和批处理。

系统是单机、单应用进程架构，不引入 Hangfire、RabbitMQ、Redis 或分布式调度。

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
- 默认并发数为 1，设置页允许调整为 1～2；
- 同优先级按创建时间 FIFO；
- 队列必须有界，避免批量导入时无限占用内存。

不要只把任务对象放进内存 Channel，否则应用退出后排队任务会丢失。

### 6.5 进度模型

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

### 6.6 工作流扩展

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

### 6.7 应用退出

关闭应用时：

1. 停止接受新任务；
2. 请求取消运行任务；
3. 等待有限的优雅退出时间；
4. 超时后终止 FFmpeg 进程树；
5. 将未正常结束的任务标记为 `Interrupted`；
6. 刷新状态和日志后退出。

不能让 Photino 窗口关闭后残留 FFmpeg 或 .NET 后台进程。

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

## 10. 错误模型与日志

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

## 11. 测试策略

### 11.1 Domain

- 时间范围验证；
- 输出路径和冲突策略；
- Job 状态转换；
- 终态不可逆；
- 重试关联。

### 11.2 Application

- 入队先持久化；
- FIFO 和并发限制；
- 取消排队任务；
- 取消运行任务；
- Worker 异常不会停止后续任务；
- 启动时修复 Interrupted；
- 重试创建新任务；
- 状态和进度事件顺序。

### 11.3 FFmpeg

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

### 11.4 Persistence

- SQLite Schema Migration；
- 并发状态更新；
- 应用重启后的队列恢复；
- Running 到 Interrupted 的修复；
- 历史和产物查询。

### 11.5 UI

使用 bUnit 覆盖：

- 表单校验；
- 创建任务；
- 任务进度和状态显示；
- 取消与重试；
- 空状态；
- 错误状态；
- Blazor Blueprint Provider 配置。

### 11.6 平台 Smoke Test

在 Windows、macOS、Linux 分别验证：

- 应用启动；
- WebView 加载；
- 文件/目录选择；
- 文件拖放；
- 视频预览；
- 启动和取消 FFmpeg；
- 打开输出目录；
- 应用关闭无残留进程；
- 深色/浅色主题；
- 中文输入法和高 DPI。

## 12. CI 与发布

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
6. 格式和分析器检查。

发布阶段：

- Windows：先提供 self-contained ZIP，再增加安装包；
- macOS：`.app` + 签名/公证，后续提供 DMG；
- Linux：先提供 tarball，再评估 AppImage 或 Flatpak；
- 每个平台发布包包含对应 FFmpeg/ffprobe 或提供受控下载流程；
- 生成第三方许可证清单和校验值。

首版优先 RID：

```text
win-x64
linux-x64
osx-x64
osx-arm64
```

## 13. 分阶段实施计划

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
- 使用 Fake Handler 完成全部任务系统测试。

验收：

- 不依赖 FFmpeg 也能完整验证任务系统；
- 应用重启后状态正确；
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

### 阶段 5：PhotinoX Shell

- 创建 `Clipify.Desktop`；
- 接入 Generic Host；
- 接入 `PhotinoX.Blazor`；
- 实现平台服务；
- 启动/停止 MediaJobWorker；
- 实现本地媒体预览资源策略。

验收：

- 同一个可执行项目能为四个首版 RID 发布；
- UI 和 Application 不引用 PhotinoX；
- 关闭应用无残留任务和进程。

### 阶段 6：Blazor Blueprint UI

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

### 阶段 7：功能对等与 WinForms 归档

- 对照基线测试；
- 修复新版本功能差异；
- 创建 WinForms 归档 Tag；
- 从活动解决方案移除 WinForms；
- 清理重复服务和旧前端构建链；
- 处理 `ClipifyConveter`：删除或改造成复用 Application 的 `Clipify.Cli`。

验收：

- 主解决方案只包含新架构；
- 不再依赖 WindowsForms；
- 不再依赖 xFFmpeg.NET、AntDesign、Flowbite；
- README 与真实构建方式一致。

### 阶段 8：发布完善

- 三平台安装和依赖检测；
- macOS 签名/公证；
- Linux 包格式；
- 崩溃日志和诊断导出；
- 自动更新方案另行 ADR。

## 14. Cursor 实施规则

交给 Cursor 实现时，应附带以下约束：

1. 每次只实施一个阶段或一个清晰子阶段。
2. 修改前先阅读本文档及涉及的现有文件。
3. 每个阶段先写或更新测试，再迁移生产代码。
4. 不在同一个提交中同时完成项目移动、架构重写和 UI 重做。
5. 不直接复制现有重复代码到新项目。
6. 不为了“以后可能有用”引入通用工作流框架。
7. 不让 UI 直接调用 FFmpeg 或 SQLite。
8. 不把 PhotinoX 类型暴露给 UI/Application。
9. 不用完整字符串拼接 FFmpeg 命令。
10. 不用 `Task<bool>` 或空 catch 隐藏错误。
11. 不使用 `async void`，UI 事件入口除外；即使是 UI 入口也应立即委托给可等待方法。
12. 每次新增 NuGet 包必须说明用途和替代方案。
13. 每个里程碑结束必须运行 Build、Tests，并更新本文档中的实际偏差。
14. 如果实现发现方案与平台现实冲突，先记录 ADR，不得静默改变架构。

建议 Cursor 每阶段输出：

- 修改摘要；
- 新增/删除项目；
- 关键设计决定；
- 执行过的命令；
- 测试结果；
- 已知问题；
- 下一阶段前置条件。

## 15. 明确的非目标

当前重构不包含：

- Android/iOS；
- MAUI 恢复；
- 自研跨平台 BlazorWebView；
- PhotinoX fork；
- 云端转码；
- 分布式任务队列；
- 通用 DAG 工作流平台；
- 多用户；
- 插件市场；
- 默认启用硬件编码；
- 完整非线性视频编辑时间线；
- 自动更新的最终选型。

这些能力必须在基础架构稳定后单独评估。

## 16. 完成定义

本轮现代化完成需同时满足：

- 使用 .NET 10；
- Windows、macOS、Linux 均由 PhotinoX 承载同一个 Blazor UI；
- UI 使用 Blazor Blueprint；
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

## 17. 参考资料

- [Microsoft：Blazor Hosting Models / Blazor Hybrid](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0)
- [PhotinoX.Blazor NuGet](https://www.nuget.org/packages/PhotinoX.Blazor)
- [PhotinoX.Blazor GitHub](https://github.com/ivanvoyager/PhotinoX.Blazor)
- [Blazor Blueprint 安装文档](https://blazorblueprintui.com/docs/installation)
- [Blazor Blueprint 组件列表](https://blazorblueprintui.com/components)
- [BlazorBlueprint.Components NuGet](https://www.nuget.org/packages/BlazorBlueprint.Components)
- [.NET Hosted Services 与有界 Channel 队列](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0)
- [FFmpeg `-progress` 官方文档](https://ffmpeg.org/ffmpeg.html)

## 18. 交给 Cursor 的首轮指令

建议不要让 Cursor 一次执行整份方案。第一轮可以直接提供以下指令：

```text
请先完整阅读 docs/modernization-plan.md、README.md、Clipify.sln，以及现有
Clipify.Core 和 Clipify.Forms 中与 FFmpeg、依赖注入、视频裁剪、音频提取有关的代码。

本轮只实施“阶段 0：建立安全基线”和“阶段 1：解决方案与 .NET 10 基础”。
不要删除 MAUI，不要迁移 UI，不要实现 FFmpeg，不要引入 PhotinoX。

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
