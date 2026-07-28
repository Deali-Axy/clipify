# Clipify 现代化 · 第 5 轮（阶段 5：共享 Hosting 与 CLI）

> 状态：规划中（交由 Cursor 实施）  
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

1. 使用官方 `System.CommandLine`；包版本由 `Directory.Packages.props` 集中管理。
2. `Clipify.Cli` 只引用 `Clipify.Hosting`，通过 Application Contract 调用业务能力。
3. Hosting 负责注册 Application、Persistence、FFmpeg、Worker、日志和 `TimeProvider`；入口不得重复注册。
4. 启动顺序固定为：解析配置 → 建 Host → 获取 Migration Lock 并迁移 → 启动 Worker → 执行命令。
5. 数据库、锁和日志使用跨平台用户数据目录；测试必须允许显式覆盖到临时目录。
6. 普通媒体命令只等待自己提交的 Job；首版不提供 `--detach`。
7. `jobs wait` 优先订阅变更并以持久化查询兜底，进程重启后仍可等待。
8. 第一次 `Ctrl+C` 请求取消当前 Job 并等待清理；再次中断允许快速退出。
9. 输出由独立 Renderer 生成，不在 Handler 中写 Console，DTO 不直接序列化 EF Entity。
10. `doctor` 只做诊断：检查 FFmpeg、ffprobe、SQLite、数据目录与平台信息，不修改用户媒体。

## 输出与退出码

- 默认：stdout 输出结果和进度；stderr 输出诊断、警告和错误；
- `--json`：stdout 只输出一个最终 JSON 对象，不显示进度条；
- `--jsonl`：stdout 每行一个事件，包含进度与最终结果；
- 日志不得污染 JSON/JSONL stdout；结构化字段使用稳定 snake_case；
- 成功、参数错误、文件/权限、工具不可用、任务失败、取消、Interrupted 使用固定 Exit Code；
- Exit Code 通过 ErrorCode/JobState 显式映射，禁止解析错误消息文本。

## 工作顺序

1. 先补 CLI 集成测试夹具、临时数据目录和 Console 捕获；
2. 完善 Hosting options、迁移入口、日志路由和可测试 Host factory；
3. 实现输出 DTO、Renderer、Exit Code 映射与公共命令管线；
4. 实现 `doctor`、`probe`；
5. 实现三类媒体命令及等待/进度/取消；
6. 实现 `jobs list/get/wait/cancel/retry`；
7. 审计 Converter，更新说明，不迁移越界转换能力；
8. 运行完整 Release 验证并回填实际结果。

## 测试重点

- 命令解析、帮助、缺参、非法时间/格式/JobId；
- 文本、JSON、JSONL 快照与 stdout/stderr 隔离；
- ErrorCode/JobState 到 Exit Code 的完整映射；
- 媒体命令提交、等待、成功、失败、取消和产物输出；
- `Ctrl+C` 后 FFmpeg 进程树和 partial 文件均被清理；
- `jobs` 跨进程读取同一 SQLite 历史；
- 两个 CLI 进程竞争同一 Job 时只执行一次；
- 空格、中文路径及 Windows/macOS/Linux 路径差异；
- 阶段 3、4 全部回归测试继续通过。

## 验收清单

- [ ] 所有命令只调用 Application Contract；
- [ ] CLI 中不存在 FFmpeg 完整命令拼接或 shell 启动；
- [ ] 普通媒体命令默认等待终态且无 `--detach`；
- [ ] JSON stdout 无日志、进度条或额外文本污染；
- [ ] JSONL 每行均为完整合法 JSON；
- [ ] Exit Code 稳定且不依赖消息文本；
- [ ] `Ctrl+C` 能请求取消并完成有限时间清理；
- [ ] `doctor` 能明确报告工具和运行目录状态；
- [ ] 两个 CLI 进程不会重复执行同一 Job；
- [ ] `ClipifyConveter` 未被复制进新架构；
- [ ] Release restore/build/test 与三平台 CI 通过。

## 验证命令

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
dotnet run --project src/Clipify.Cli -- --help
dotnet run --project src/Clipify.Cli -- doctor --json
```

## 下一轮

阶段 6：MCP Server。复用本阶段 Hosting、输出边界和任务合同，实现受限文件系统下的 stdio Tools。
