# Clipify 现代化 · 第 5 轮（阶段 5：共享 Hosting 与 CLI）

> 状态：已关闭（待合入 `modernization/trunk`）  
> 日期：2026-07-28  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 前置：[modernization-plan-04.md](./modernization-plan-04.md) 已关闭  
> 工作分支：`modernization/phase-5-cli`（从 `modernization/trunk` 派生）

## 本轮目标

完善现有 `Clipify.Hosting` 组合根，建立跨平台 `clipify` CLI，使命令行复用 Application、SQLite 任务系统和 FFmpeg 基础设施。

CLI 提供稳定的人类文本、JSON、JSONL、退出码和取消语义，可供用户、脚本与 CI 使用。

## 边界

本轮要做：

- 统一 Hosting 的配置、日志、数据库迁移、Worker 启停和应用目录约定；
- 实现 `doctor`、`probe`、三类媒体命令及 `jobs` 命令组；
- 普通媒体命令提交 Job 后等待终态并展示进度；
- 建立文本、JSON、JSONL 输出和稳定 Exit Code；
- 实现 `Ctrl+C` 取消与自动化测试；
- 审计 `ClipifyConveter`，只迁移仍适用的 CLI 行为约定。

本轮不做：

- 不实现 `convert`、`merge`、`batch`、Daemon、MCP、GUI 或发布安装；
- 不增加原始 FFmpeg 参数入口，不在 CLI 拼接 FFmpeg 命令；
- 不复制 `ClipifyConveter` 的交互式选择和批量转换实现；
- 不改写阶段 3 的任务状态机、租约、锁或阶段 4 的提交语义；
- 不删除旧 Converter 源码，最终归档删除留待阶段 9。

## 命令范围

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

媒体命令默认不覆盖输出；冲突策略只能映射 Domain 已有白名单值。时间、格式、JobId 和路径应在命令边界完成解析，再构造强类型 Definition。

## 关键设计决定

1. 使用官方 `System.CommandLine` 2.0.10；包版本由 `Directory.Packages.props` 集中管理。
2. `Clipify.Cli` 只引用 `Clipify.Hosting`，通过 Application Contract 调用业务能力。
3. Hosting 负责注册 Application、Persistence、FFmpeg、Worker、日志和 `TimeProvider`；入口不得重复注册。
4. 启动顺序固定为：解析配置 → 建 Host → 获取 Migration Lock 并迁移 → 启动 Worker → 执行命令。
5. 数据库、锁和日志使用跨平台用户数据目录；测试必须允许显式覆盖到临时目录（`--data-dir` / `CLIPIFY_DATA_DIR`）。
6. 普通媒体命令只等待自己提交的 Job；首版不提供 `--detach`。
7. `jobs wait` 优先订阅变更并以持久化查询兜底，进程重启后仍可等待。
8. 第一次 `Ctrl+C` 请求取消当前 Job 并等待清理；再次中断允许快速退出。
9. 输出由独立 Renderer 生成，不在 Handler 中写 Console，DTO 不直接序列化 EF Entity。
10. `doctor` 只做诊断：检查 FFmpeg、ffprobe、SQLite、数据目录与平台信息，不修改用户媒体。

## 本轮结果摘要

- **Hosting**：`ClipifyAppPaths`、`ClipifyHostOptions`、`ClipifyHostFactory`（路径→建 Host→迁移→启 Worker）、文件日志、`IClipifyDoctor`
- **CLI**：`System.CommandLine` 命令树；`ExitCodeMapper`；Text/JSON/JSONL Renderer；边界解析（时间/格式/冲突策略/JobId）；`JobWaiter`；Ctrl+C 取消钩子
- **命令**：doctor、probe、trim、extract-audio、thumbnail、jobs list/get/wait/cancel/retry
- **Converter 审计**：未迁移交互式批量转换、原始 FFmpeg 拼接、自动覆盖与硬件编码；旧项目保留至阶段 9
- **测试**：51 个 CLI 测试（含真实双进程 Claim、无效 data-dir doctor、普通命令 Host 启动失败 JSON、时间溢出、文本日志隔离）

## 验收清单

- [x] 所有命令只调用 Application Contract；
- [x] CLI 中不存在 FFmpeg 完整命令拼接或 shell 启动；
- [x] 普通媒体命令默认等待终态且无 `--detach`；
- [x] JSON stdout 无日志、进度条或额外文本污染；
- [x] JSONL 每行均为完整合法 JSON；
- [x] Exit Code 稳定且不依赖消息文本；
- [x] `Ctrl+C` 能请求取消并完成有限时间清理；（钩子 + `jobs cancel` 自动化覆盖）
- [x] `doctor` 能明确报告工具和运行目录状态；
- [x] 两个 CLI 进程不会重复执行同一 Job；
- [x] `ClipifyConveter` 未被复制进新架构；
- [x] Release restore/build/test 与三平台 CI 通过。（本地 Release 全绿；CI 待 PR）

## 验证命令与本地结果

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
dotnet run --project src/Clipify.Cli -- --help
dotnet run --project src/Clipify.Cli -- doctor --json
```

本地结果（2026-07-28，Windows）：

- `ffmpeg` / `ffprobe`：**8.1.2**
- Release build 成功（Forms 既有警告，新项目 0 warnings/errors）
- 测试 **171** 通过（Domain 46 + Application 22 + FFmpeg 36 + Persistence 14 + Cli 51 + skeleton 2）
- `clipify doctor --json`：ok，报告数据目录、SQLite、ffmpeg/ffprobe 版本
- 媒体命令：trim / extract-audio / thumbnail 与中文路径通过；无 `--detach`/`convert`/`merge`/`batch`

## Codex 审阅修复（2026-07-28）

已修复并补测：

1. **[P1] JSON 参数错误**：Invoke 前拦截 `ParseResult.Errors`，按 `--json`/`--jsonl` 渲染结构化 Validation 错误并返回 Exit Code 2，不再把帮助文本写到 stdout。
2. **[P1] doctor 与迁移解耦**：`BuildForDiagnostics` + `EnsureDiagnosticsHost` 不先迁移/启 Worker；损坏的 `jobs.db` 作为 `sqlite` 检查失败项出现在 doctor JSON 中。
3. **[P1] 无效 data-dir**：Host 构建前 `TryPrepareDataRoot`；路径是文件/不可创建时输出结构化 doctor JSON（`Internal`/1），不抛空 stdout。
4. **[P2] jobs wait 缺失 Job**：`JobWaiter` 抛出 `ClipifyException(NotFound)`，渲染结构化结果，Exit Code 映射为 FileError(3)。
5. **[P2] JobWaiter 收敛**：`WhenAny` 后取消并 `WhenAll` 等待两路分支结束，避免 JSONL 在 `result` 后继续写 progress。
6. **[P2] 双 CLI Claim 竞争**：进程内双 Host + 真实 `dotnet clipify.dll` 双进程 `jobs wait`；断言单条 `fake_output` Artifact。
7. **[P2] 文本模式日志**：CLI 始终 `SuppressConsoleLogging`，Host/EF 日志不进入 stdout。
8. **[P2] 时间溢出**：`TimeSpan.FromMilliseconds` 溢出映射 Validation/2。
9. **[P2] doctor 错误码**：顶层 `error.code` 与 Exit Code 一致（路径/SQLite→`Internal`/1；仅工具→`FfmpegUnavailable`/4）。
10. **[P3]** 去掉 FailValidation/probe 失败路径上重复的 stderr `WriteError`。
11. **[P1] 普通命令启动失败 JSON 契约**：顶层 catch 经 Renderer 输出 Internal JSON/JSONL；`EnsureHostStartedAsync` 预检 data-dir 并将 Host 启动异常包装为 `ClipifyException`；补 `jobs list --json` 在文件 data-dir / 损坏 DB 下的回归。

## 已知问题 / 偏差

- `Ctrl+C` 真实控制台二次中断的端到端断言依赖交互式 Console；自动化以 CancelKeyPress 钩子 + `jobs cancel` 终态覆盖为主。
- `jobs retry` 首版在 CLI 侧仍等待新 Job 终态（与普通媒体命令一致），未提供 detach。
- 旧 `ClipifyConveter` 源码仍在解决方案中，仅作行为参考，阶段 9 再归档删除。

## 下一轮

阶段 6：MCP Server。复用本阶段 Hosting、输出边界和任务合同，实现受限文件系统下的 stdio Tools。
