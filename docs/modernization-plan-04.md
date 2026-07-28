# Clipify 现代化 · 第 4 轮（阶段 4：FFmpeg 基础设施）

> 状态：已关闭（待合入 `modernization/trunk`）  
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

## 关键设计决定

1. Domain 增加 `MediaInfo`/`MediaStreamInfo` 与白名单 Definition：`trim_media`、`extract_audio`、`thumbnail`；继续保留 `fake_delay`。
2. Application 定义 FFmpeg 端口与 `IProbeMediaUseCase`；实现放在 `Clipify.FFmpeg`，Application 不引用 FFmpeg 项目。
3. 所有进程 `UseShellExecute = false`，参数经 `ArgumentList`；禁止完整命令字符串与 shell。
4. stdout 只解析 `-progress pipe:1` 的 `key=value`；stderr 仅诊断日志。
5. 进度优先 `out_time_us`，兼容 `out_time_ms`，Fraction 限制在 `[0,1]`；SQLite 进度写入约 500ms 节流。
6. Runner 并发排空 stdout/stderr；取消宽限后 `Kill(entireProcessTree: true)`。
7. 输出写同目录 `.<name>.clipify-<jobId>.partial.<ext>`，验证后再提交；Fail/Overwrite/Rename/Skip 严格执行，`-y` 仅用于临时文件。
8. Locator 延迟解析；非媒体 Job / 阶段 3 测试不要求本机 FFmpeg。
9. `ClipifyException` 携带稳定错误码；Executor 写入 DB 的短消息不含完整 stderr。
10. Hosting 通过 `AddClipifyFFmpeg` 组合注册三个真实 Handler，并保留 Fake Handler。

## 本轮结果摘要

- Domain：媒体元数据、三种真实 Definition、Serializer 白名单扩展
- Application：FFmpeg 端口、`ProbeMediaUseCase`、`ClipifyException`/错误码、Dispatcher 支持真实 Handler
- Clipify.FFmpeg：Locator、VersionProbe、ProgressParser、ProcessRunner（可替换进程工厂）、FFprobeClient、CommandBuilders、OutputCommitter、三个 Handler、`AddClipifyFFmpeg`
- Hosting：`AddClipifyMediaJobs` 组合注册 FFmpeg
- 测试：Parser/Builder/Runner/Probe/Output + ProcessHost 夹具 + lavfi 真实 E2E（含中文路径与取消清理）

## 验收清单

- [x] `src/` 与 `tests/` 不引用 `xFFmpeg.NET`，旧项目依赖保持不动；
- [x] 三类 Definition 可持久化、重载、重试并由 Worker 正确分派；
- [x] 所有参数通过 `ArgumentList`，路径字符测试通过；
- [x] 进度来自 `-progress`，不从普通 stderr 猜测；
- [x] ffprobe 只向上层返回 Clipify 自有模型；
- [x] 正常完成产生已验证正式文件和一条 Artifact；
- [x] Fail/Overwrite/Rename/Skip 行为及竞态测试通过；
- [x] 失败、取消不会留下正式输出或临时文件；
- [x] 取消能在有限时间内终止整个进程树；
- [x] 真实 trim/extract-audio/thumbnail E2E 与中文路径通过；
- [x] 阶段 3 全部测试继续通过；
- [ ] Release restore/build/test 和三平台 CI 通过。（本地 Windows Release 已通过；macOS/Ubuntu 随 PR 验证）

## 验证命令与本地结果

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
```

本地结果（2026-07-28，Windows，含提交边界闭合）：

- `ffmpeg` / `ffprobe`：**8.1.2**（gyan.dev full_build，scoop）
- Release build 成功（Forms 既有警告，0 errors）
- 测试 **114** 通过（Domain 46 + Application 15 + FFmpeg 36 + Persistence 14 + skeleton 3）
- 真实 E2E：tools 版本、trim/extract/thumbnail（含 ffprobe 产物校验）、中文路径、取消后 PID 退出与无正式/partial 残留
- 提交边界竞态：Commit 后取消 → Succeeded；Commit 前取消 → Canceled；提交后元数据异常 → Succeeded

## 已知问题 / 偏差

- FFprobe 为获取 stdout JSON，在 `FFprobeClient` 内使用与 Runner 相同安全规则的独立进程启动；尚未把“捕获 stdout 文本”并入通用 `IFFmpegProcessRunner`（进度协议仍走 Runner）。
- 三平台 CI 与发布包内 FFmpeg 分发仍属后续工作。

## Codex 审阅修复（2026-07-28）

已修复并补测：

1. **提交边界**：verify 通过后 Commit/Artifact/completed 使用 `CancellationToken.None`；`OutputPreparation.Committed` 后 Cleanup 不再删除正式输出。
2. **Overwrite**：同目录 `File.Replace` + backup 恢复；锁定目标失败时保留原文件。
3. **`out_time_ms`**：按微秒 PTS 解析（历史误名）。
4. **verify**：提交前 ffprobe 临时产物；trim 校验时长、extract 校验音轨、thumbnail 校验可读帧。
5. **Runner**：正常/取消路径均读到 EOF；VersionProbe/FFprobe 取消后 Kill 并等待退出。
6. **时间戳**：`FormatTimestamp` 使用总小时数，覆盖 >24h。
7. **取消 E2E**：快照本次 ffmpeg PID 并断言退出；ProcessHost 回传 parent/child PID。
8. **提交边界闭合（二次复核）**：`MediaJobExecutionContext.MarkOutputCommitted()`；Executor 在已提交时强制 `Succeeded`（含从 `Canceling`）；允许 `Canceling → Succeeded`；Barrier 竞态测试覆盖 Commit 前后取消与提交后异常。
9. **提交后元数据幂等**：`PostCommitFinalizer` 在重试循环外创建稳定 ArtifactId；进度与 Artifact 分开重试；`AddArtifactAsync` 按 ArtifactId 幂等；永久元数据失败时写入可查询的 `PostCommitWarning`。

## 下一轮

阶段 5：共享 Hosting 与 CLI。复用本阶段 FFmpeg/Job 合同实现 `clipify` 命令、文本/JSON/JSONL 输出与 doctor。
