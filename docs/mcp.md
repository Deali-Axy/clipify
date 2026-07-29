# Clipify MCP Server

`clipify-mcp` 是本地 stdio MCP Server。它复用 `Clipify.Hosting`、Application Contract、SQLite Job Store 与 FFmpeg，向 Agent 暴露媒体探测与异步任务能力。

## 能力概览

| Tool | 行为 |
|---|---|
| `probe_media` | 同步探测媒体信息 |
| `trim_video` | 入队裁剪任务，立即返回 `job_id` |
| `extract_audio` | 入队音频提取任务 |
| `generate_thumbnail` | 入队缩略图任务 |
| `list_jobs` | 分页列出任务 |
| `get_job` | 查询单个任务与有限 Artifact |
| `wait_job` | 有限等待（默认 15s，最大 60s） |
| `cancel_job` | 请求取消任务 |
| `retry_job` | 基于失败/中断/取消任务创建新任务 |

媒体 Tool **不会**在一次调用中等待 FFmpeg 完成。取消 Tool Call 只停止当前探测/等待/提交，**不会**隐式取消已入队 Job。

## 运行

```bash
dotnet run --project src/Clipify.Mcp -- --allow-root <media-workspace>
```

常用参数：

- `--allow-root <path>`：允许访问的媒体根目录（可重复）
- `--data-dir <path>`：覆盖 Job/日志数据目录
- `--no-file-log`：关闭数据目录文件日志

环境变量：

- `CLIPIFY_DATA_DIR`：默认数据目录
- `CLIPIFY_ALLOWED_ROOTS`：额外允许根，使用操作系统 `PATH` 分隔符分隔

未指定 `--allow-root` 且环境变量为空时，默认允许当前工作目录。

## 安全边界

- 输入必须是允许根内的已存在文件
- 输出父目录必须已存在；首版不会自动创建目录
- 默认冲突策略为 `fail`；覆盖需显式 `conflict_policy=overwrite`
- 禁止 URL / 网络下载、任意 Shell、原始 FFmpeg 参数、删除文件
- 越界错误只返回稳定错误码，不泄露允许根之外的规范路径
- stdout 仅承载 MCP JSON-RPC；日志写 stderr 或数据目录日志

## 通用 MCP 客户端配置

```json
{
  "mcpServers": {
    "clipify": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "/absolute/path/to/clipify-mcp.dll",
        "--allow-root",
        "/absolute/path/to/media-workspace"
      ],
      "env": {}
    }
  }
}
```

也可直接指向发布后的 `clipify-mcp` 可执行文件。不要把本机绝对路径提交进仓库。

## Cursor 示例

仓库提供模板：[`docs/examples/cursor-mcp.json`](./examples/cursor-mcp.json)。复制到项目 `.cursor/mcp.json` 后，把路径替换为本地构建产物与媒体工作区：

```bash
dotnet build src/Clipify.Mcp/Clipify.Mcp.csproj -c Release
```

## 与 CLI 共享任务历史

GUI / CLI / MCP 共用同一 SQLite Job Store（由 `CLIPIFY_DATA_DIR` / `--data-dir` 决定）。多个进程可同时观察任务，但同一 Job 只会由一个 Worker 租约执行。

## 响应形状

Tool 文本结果为统一 JSON 信封：

```json
{
  "ok": true,
  "job_id": "...",
  "state": "queued",
  "result": {},
  "error": null,
  "warnings": null
}
```

时间参数接受整数毫秒或 `HH:MM:SS[.fff]`。
