# Clipify 现代化 · 第 6 轮（阶段 6：MCP Server）

> 状态：实施中  
> 日期：2026-07-29  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 前置：[modernization-plan-05.md](./modernization-plan-05.md) 已关闭  
> 工作分支：`modernization/phase-6-mcp`（从 `modernization/phase-5-cli` 派生；待阶段 5 合入 trunk 后变基）

## 本轮目标

把现有 `Clipify.Mcp` 骨架实现为本地 stdio MCP Server，复用 `Clipify.Hosting`、Application Contract、SQLite Job Store 和 FFmpeg 基础设施。

Agent 可以探测媒体、提交三类媒体任务并查询、等待、取消或重试 Job；所有文件访问受允许根目录限制。

## 边界

本轮要做：

- 引入官方 C# MCP SDK，注册 stdio Transport 和九个稳定 Tools；
- 建立独立的 MCP DTO、Schema、错误映射和响应大小上限；
- 媒体 Tool 只入队并立即返回 JobId，不在一次调用中等待 FFmpeg；
- 实现有限等待、客户端取消和结构化 Job/Artifact 响应；
- 实现 `--allow-root`、环境变量、路径规范化和链接逃逸防护；
- 保证协议 stdout 与日志 stderr/文件严格分离；
- 提供通用 MCP Client 与 Cursor 项目配置示例；
- 建立 Schema、安全、协议和真实 Job 流程测试。

本轮不做：

- 不实现 HTTP/SSE、认证、Daemon、MCP Resources、Prompts 或协议 Tasks 扩展；
- 不暴露 Shell、原始 FFmpeg 参数、URL 下载、任意文件读写或删除工具；
- 不返回媒体二进制、完整日志或无上限的列表/错误文本；
- 不复制 CLI Handler、Renderer 或业务校验到 MCP；
- 不修改阶段 3 的状态机、租约、锁及阶段 4 的提交语义；
- 不实现 GUI、PhotinoX 或发布安装。

## Tool 范围

| Tool | 行为 | 核心参数 |
|---|---|---|
| `probe_media` | 同步只读 | `input` |
| `trim_video` | 创建 Job | `input`、`output`、`start`、`end`、`conflict_policy` |
| `extract_audio` | 创建 Job | `input`、`output`、`format`、`conflict_policy` |
| `generate_thumbnail` | 创建 Job | `input`、`output`、`at`、`format`、`conflict_policy` |
| `list_jobs` | 分页只读 | `states`、`skip`、`take` |
| `get_job` | 只读 | `job_id` |
| `wait_job` | 有限等待 | `job_id`、`timeout_seconds` |
| `cancel_job` | 请求取消 | `job_id` |
| `retry_job` | 创建新 Job | `job_id` |

Tool 名称和字段使用 snake_case。时间接受整数毫秒或 `HH:MM:SS.fff`；枚举只接受 Domain 白名单值。

## 关键设计决定

1. 使用稳定版 `ModelContextProtocol` 1.4.1 并由 `Directory.Packages.props` 集中管理；不使用 2.0 预发布版或 Tasks 扩展。
2. `Clipify.Mcp` 只引用 `Clipify.Hosting`；Tool 通过 Application Contract 调用能力，不直接访问 EF Core 或 FFmpeg。
3. 使用 SDK Attribute Tool 与自动 Schema，另做 Schema 快照防止名称、必填项、类型和描述意外漂移。
4. Host 启动顺序沿用阶段 5：解析 MCP 参数 → 建 Host → 迁移 → 启 Worker → 运行 stdio Server。
5. stdout 仅供 MCP JSON-RPC；日志、诊断和启动错误写 stderr 或数据目录日志，禁止 `Console.WriteLine`。
6. 统一响应包含 `ok`、`job_id`、`state`、`result`、`error`、`warnings`；DTO 不直接序列化 Domain/EF Entity。
7. 媒体 Tool 入队成功即返回；`wait_job` 默认 15 秒、最大 60 秒，超时返回最新 Snapshot，不视为 Job 失败。
8. Tool Call 被取消只停止当前探测/等待/提交调用，不隐式取消已入队 Job；取消 Job 必须调用 `cancel_job`。
9. `list_jobs` 默认 20、最大 100，使用现有 Skip/Take；`get_job` 返回有限 Artifact 和错误摘要。
10. MCP 边界复用或提取 CLI 的时间、JobId、格式、冲突策略解析规则，避免产生两套语义。
11. Tool Annotations 是风险提示而非授权；服务端路径策略和 Application 校验始终执行。
12. 首版不依赖客户端 Roots；Roots 未来只能补充允许范围，不能扩大启动时配置的安全边界。

## Tool Annotations

- `probe_media`、`list_jobs`、`get_job`、`wait_job`：`readOnlyHint=true`、`openWorldHint=false`；
- 三个媒体 Tool：`readOnlyHint=false`、`idempotentHint=false`、`openWorldHint=false`；
- 媒体 Tool 支持 Overwrite，故保守设置 `destructiveHint=true`，默认冲突策略仍为 Fail；
- `cancel_job`：`destructiveHint=true`；`retry_job`：`idempotentHint=false`；
- 注解、标题和描述纳入快照测试。

## 文件系统安全

1. 默认允许根目录为 Server 启动时的当前工作目录；可重复使用 `--allow-root <path>` 增加根目录。
2. `CLIPIFY_ALLOWED_ROOTS` 提供非敏感配置回退；命令行与环境变量合并、去重，空项视为配置错误。
3. 启动时把允许根目录解析为绝对规范路径；根不存在、不是目录或无法解析时拒绝启动。
4. 输入必须存在且为文件；输出父目录必须已存在，首版不由 MCP 创建目录。
5. 对输入、输出及已存在的父链解析符号链接/目录联接后再校验；阻止 `..`、链接和 Junction 逃逸。
6. Windows 比较忽略大小写并处理驱动器/UNC；Unix 使用大小写敏感比较，禁止仅靠字符串前缀判断。
7. 输出目标即使尚不存在，也校验真实父目录和最终组合路径；默认 Fail，Overwrite 必须显式传入。
8. 越界错误只返回稳定错误码和安全摘要，不泄露允许根目录之外的规范路径。

## 工作项

1. 更新中央包版本和 `Clipify.Mcp.csproj`，替换 skeleton 入口并接入 Hosting。
2. 增加 MCP options/参数解析、allowed-root policy、路径解析结果和安全错误。
3. 增加 Tool 参数/响应 DTO、Mapper、Error Mapper 和输出截断策略。
4. 实现九个 Tool；媒体定义只使用 Domain 强类型，不接受任意 JSON Definition。
5. 实现 `wait_job` 的订阅优先、持久化查询兜底及有限超时；进程重启后仍可等待。
6. 增加 Tool Schema/Annotation 快照、单元测试、stdio 参考 Client 集成测试。
7. 增加路径穿越、前缀碰撞、大小写、UNC、符号链接和 Junction 安全回归测试。
8. 增加入队→wait→Artifact、取消、重试、双进程 Claim 及 stdout 污染测试。
9. 新增 `docs/mcp.md`，提供通用配置和 `.cursor/mcp.json` 示例，不提交用户绝对路径。

## 验收清单

- [x] MCP Client 可完成初始化、Tool Discovery，并调用全部九个 Tools；
- [x] stdout 每条内容均属于 MCP 协议，日志和异常不会污染协议流；
- [x] Tool Schema、名称、描述、必填项和 Annotations 与快照一致；
- [x] 媒体 Tool 快速返回 JobId，`wait_job` 不超过 60 秒且超时返回最新状态；
- [x] Tool Call 取消、Job 取消和进程终止三种语义互不混淆；
- [x] 输入、输出、`..`、链接、Junction、UNC 和前缀碰撞不能逃出允许根目录；
- [x] 列表、Artifact、错误和日志摘要均有稳定上限；
- [x] MCP 与 CLI 共享数据库，两个进程不会重复执行同一 Job；
- [x] 不存在 Shell、原始 FFmpeg、URL、删除文件或任意 Definition 入口；
- [ ] Release restore/build/test 与 Windows、macOS、Linux CI 通过。

## 验证命令

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
dotnet run --project src/Clipify.Mcp -- --allow-root <media-workspace>
```

额外使用 SDK 的 `McpClient` 通过 stdio 启动真实 `clipify-mcp`，验证 Discovery、probe、入队、wait、cancel、retry 和协议 stdout。

## 交给 Cursor 的实施指令

先阅读本文件、完整方案 §11/§13.7/§15、阶段 5 文档及现有 MCP skeleton。严格按“包与入口 → 安全边界 → DTO/Tools → 协议测试 → 文档”顺序实施；每完成一层先运行相关测试，不得扩大到阶段 7。

## 已知风险 / 前置

- 阶段 5 需先合入 `modernization/trunk`，阶段 6 分支不得直接依赖未合入的临时改动；
- Windows Junction/UNC 测试可能需要平台条件或权限探测，跳过时必须记录原因并保留可运行覆盖；
- SDK 2.0 仍在预发布，本阶段锁定 1.4.1；后续升级必须独立评估协议与 Schema 差异。

## 参考

- [MCP C# SDK：Getting Started](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)
- [MCP C# SDK：Tools](https://csharp.sdk.modelcontextprotocol.io/concepts/tools/tools.html)
- [ModelContextProtocol 1.4.1](https://www.nuget.org/packages/ModelContextProtocol/1.4.1)

## 下一轮

阶段 7：PhotinoX Shell。复用本阶段 Hosting 与共享 Job 历史，建立跨平台桌面宿主。
