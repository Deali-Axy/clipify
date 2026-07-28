# Clipify 现代化 · 第 4 轮（阶段 4：FFmpeg 基础设施）

> 状态：待实施  
> 日期：2026-07-28  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 前置：[modernization-plan-03.md](./modernization-plan-03.md) 已关闭并合入 `modernization/trunk`  
> 工作分支：`modernization/phase-4-ffmpeg`（从 `modernization/trunk` 派生）

## 本轮目标

在新架构中建立不依赖 `xFFmpeg.NET` 的 FFmpeg/ffprobe 基础设施，并把视频裁剪、音频提取、缩略图接入阶段 3 的持久化任务系统。

完成后，真实媒体任务应具备强类型参数、机器可读进度、可靠取消、进程树清理、临时输出提交和 Artifact 记录。

## 边界

本轮要做：

- 扩展 Domain/Application 的媒体模型、真实 Job Definition 和 FFmpeg 端口；
- 在 `Clipify.FFmpeg` 实现 locator、version probe、runner、progress parser、ffprobe、command builder 与 output committer；
- 实现 trim、extract-audio、thumbnail 三个 Job Handler；
- 最小扩展 Hosting 注册，使 Worker 能执行真实 Handler；
- 建立单元、进程集成和真实媒体端到端测试。

本轮不做：

- 不迁移 Forms 页面，不实现 CLI、MCP、PhotinoX 或新 UI；
- 不增加转码预设、硬件编码、批处理、Daemon 或任意原始参数入口；
- 不决定发布包内 FFmpeg 的来源与许可证方案，该项留待发布阶段；
- 不删除旧 `Clipify.Core/Forms` 的 `xFFmpeg.NET`；旧项目仅作行为参考，活动新代码 `src/` 不得引用它；
- 不改写阶段 3 的 Claim、Lease、Heartbeat、Job Lock 和状态机语义。

## 设计约束

1. `Domain` 只保存 Clipify 自有模型；不得出现 `Process`、ffprobe DTO 或第三方类型。
2. `Application` 定义端口和任务编排；不得引用 `Clipify.FFmpeg`。
3. 所有进程使用 `ProcessStartInfo.UseShellExecute = false`，参数逐项加入 `ArgumentList`。
4. 禁止接收或拼接完整 FFmpeg 命令字符串，禁止经 shell 执行。
5. stdout 只解析 `-progress pipe:1` 的 `key=value`；stderr 只作诊断日志。
6. 进度优先使用 `out_time_us`，兼容 `out_time_ms`，并限制 Fraction 到 `[0,1]`。
7. runner 必须并发排空 stdout/stderr，取消超时后 `Kill(entireProcessTree: true)` 并等待退出。
8. 输出先写最终目录中的 JobId 临时文件，验证成功后再提交；失败或取消必须清理。
9. 冲突策略严格执行现有 `Fail/Overwrite/Rename/Skip`；不得默认无条件加入 `-y`。
10. 时间、数字和 JSON 解析使用 invariant culture；路径按本机绝对路径处理。
11. 错误向任务系统暴露稳定错误码与简短消息；完整 stderr 写日志，不塞入数据库错误字段。
12. locator 延迟解析二进制，使非媒体 Job 和阶段 3 测试不要求本机已安装 FFmpeg。

## 合同与代码落点

### Domain / Application

- 增加 `MediaInfo`、`MediaStreamInfo` 等自有元数据模型。
- 增加白名单 Definition：`trim_media`、`extract_audio`、`thumbnail`。
- Definition 仅接受业务字段：输入、输出、时间范围/时间点、冲突策略和有限枚举选项；不接受原始参数。
- 扩展 `MediaJobDefinitionSerializer` 和 Dispatcher，同时保留 `fake_delay` 供任务系统测试。
- Application 增加 `IFFprobeClient` 等必要端口及面向调用方的媒体探测用例。
- Handler 通过 `MediaJobExecutionContext` 报告进度和 Artifact，不直接写 Job 状态。

### Clipify.FFmpeg

- `FFmpegLocator`：按显式配置、应用本地工具目录、PATH 的顺序解析 `ffmpeg`/`ffprobe`。
- `FFmpegVersionProbe`：执行 `-version`，返回自有版本/诊断模型，并区分缺失、不可执行和版本失败。
- `FFmpegProgressParser`：增量解析行、忽略未知键，在 `progress=continue|end` 时生成快照。
- `FFmpegProcessRunner`：启动、双流读取、日志摘要、退出码、取消与进程树清理。
- `FFprobeClient`：执行 JSON 探测并映射 Domain 模型，容忍可选字段但拒绝无效 JSON。
- 三个强类型 command builder：共享安全公共参数，各自只生成 `IReadOnlyList<string>`。
- `OutputCommitter`：准备临时路径、处理冲突、验证并原子提交或清理。
- 三个 Handler：依次执行 validate → probe → prepare → run → verify → commit → artifact。
- 提供 `AddClipifyFFmpeg(options)`；Hosting 只做组合注册，不包含命令构建逻辑。

## 实施顺序

1. 先为新模型、Definition 白名单和参数列表写失败测试。
2. 完成 Domain/Application 合同，确认依赖方向与序列化往返测试通过。
3. 实现 locator、version probe 和 progress parser。
4. 实现可替换进程边界的 runner，并完成退出、双流、取消和子进程清理测试。
5. 实现 ffprobe DTO（仅限 FFmpeg 项目）到 Domain 模型的映射。
6. 实现三个 command builder，覆盖空格、中文、引号和特殊字符路径。
7. 实现 output committer 与四种冲突策略，覆盖失败/取消清理。
8. 实现三个真实 Handler、进度节流和 Artifact 写入。
9. 扩展 DI/Dispatcher，以阶段 3 Worker 运行真实任务；保持 Fake Handler 回归测试通过。
10. 加入真实 FFmpeg 端到端测试，最后运行全解决方案验证并记录偏差。

## 测试要求

- Parser：分块输入、CRLF/LF、未知/畸形键、速度、结束帧和缺失总时长。
- Builder：断言参数数组而非渲染后的字符串；覆盖中文、空格、引号和 shell 元字符。
- Runner：非零退出、超长 stderr、stdout/stderr 并发、取消竞态、子进程树退出。
- ffprobe：多音视频流、无音轨、可选字段缺失、无效 JSON、非零退出。
- Output：四种冲突策略、同目录临时文件、验证失败、取消和提交后清理。
- Handler：阶段顺序、进度、Artifact、稳定错误映射，且不绕过 `IMediaJobService`。
- 真实 E2E：用 lavfi 在临时目录生成微型媒体，验证裁剪、提取音频、缩略图和中文路径。
- 真实 E2E 再验证取消后无 FFmpeg/子进程残留、无正式输出、无 `.partial` 文件。
- CI 应显式提供并验证 FFmpeg/ffprobe；缺失时测试失败并给出诊断，不静默跳过。
- 测试产物只进入临时目录，不提交有版权风险的大型媒体二进制。

## 验收清单

- [ ] `src/` 与 `tests/` 不引用 `xFFmpeg.NET`，旧项目依赖保持不动；
- [ ] 三类 Definition 可持久化、重载、重试并由 Worker 正确分派；
- [ ] 所有参数通过 `ArgumentList`，路径字符测试通过；
- [ ] 进度来自 `-progress`，不从普通 stderr 猜测；
- [ ] ffprobe 只向上层返回 Clipify 自有模型；
- [ ] 正常完成产生已验证正式文件和一条 Artifact；
- [ ] Fail/Overwrite/Rename/Skip 行为及竞态测试通过；
- [ ] 失败、取消不会留下正式输出或临时文件；
- [ ] 取消能在有限时间内终止整个进程树；
- [ ] 真实 trim/extract-audio/thumbnail E2E 与中文路径通过；
- [ ] 阶段 3 全部测试继续通过；
- [ ] Release restore/build/test 和三平台 CI 通过。

## 验证命令

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
```

另记录 `ffmpeg -version`、`ffprobe -version`、实际 E2E 数量、取消清理检查和三平台差异。

## 交给 Cursor 的执行指令

完整阅读本文件、`docs/modernization-plan.md` 的 §6/§7/§13.3/阶段 4、`docs/modernization-plan-03.md`，以及阶段 3 的 Definition、Dispatcher、Executor、Hosting 与测试。

只在 `modernization/phase-4-ffmpeg` 实施本计划；先报告分支/工作树、可用 FFmpeg 版本和准确文件变更清单，再按“实施顺序”小步提交。不得迁移 UI/CLI/MCP，不得删除旧项目或扩大到阶段 5。每个里程碑运行相关测试；发现跨平台行为或架构冲突时先记录并报告，不得静默改变边界。完成后输出修改摘要、设计决定、命令与测试结果、已知问题及阶段 5 前置条件。
