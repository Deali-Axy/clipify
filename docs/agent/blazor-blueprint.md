# Blazor Blueprint Agent 使用指引

本文档用于实现和维护 `Clipify.UI`。Blazor Blueprint MCP 是开发期文档工具，不是 Clipify 产品的一部分，也不是规划中的 `clipify-mcp`。

## 接入

前置条件：

- Node.js 18 或更高版本；
- Agent 客户端支持 stdio MCP；
- 首次运行允许访问网络，后续可使用 MCP 的本地文档缓存。

根据开发机平台选择模板：

- Windows：`docs/agent/blazor-blueprint-mcp.windows.json`
- macOS/Linux：`docs/agent/blazor-blueprint-mcp.unix.json`

将对应模板复制到 Agent 客户端的项目级配置位置，并按客户端要求批准该 MCP Server：

- Cursor：`.cursor/mcp.json`
- Claude Code：仓库根目录 `.mcp.json`

仓库不直接提交活动配置，因为 Windows 必须使用 `cmd /c` 包装 `npx`，而 macOS/Linux 直接执行 `npx`。两份模板的 `mcpServers` 内容相同，只有启动命令存在平台差异。

模板当前使用 `@blazorblueprint/mcp@latest` 以遵循官方安装方式，并将文档锁定到 Blazor Blueprint v3。正式开始 UI 阶段时，应：

1. 以 `Directory.Packages.props` 中 `BlazorBlueprint.Components` 的主版本为准；
2. 将 `BLAZORBLUEPRINT_VERSION` 设置为相同主版本；
3. 评估并记录 MCP npm 包的已验证版本；若 npm 包提供可用的固定版本，则用它替换 `@latest`，提高可复现性；
4. Blueprint 升级后同时更新 NuGet 版本、MCP 文档版本和本文档。

不要把 MCP 的缓存、Node.js 或 npm 包带入 Clipify 发布产物。

## Agent 查询顺序

修改 `Clipify.UI` 前，Agent 应按以下顺序工作：

1. 调用 `get_version`，确认 MCP 文档版本与项目使用的 Blueprint 主版本一致；
2. 首次接入或调整 Provider、CSS、主题时调用 `get_setup`；
3. 不确定组件选型时先调用 `search_components` 或 `list_components`；
4. 写 Razor 前调用 `get_component`，核对参数、事件、命名空间和示例；
5. 处理表单、主题、数据绑定等横切问题时调用 `get_patterns`；
6. 使用图标前调用 `get_icons`，避免猜测 Lucide 图标名称；
7. 采用 Blueprint 模板前调用 `list_blueprints` 和 `get_blueprint`，但只提取适合 Clipify 的结构；
8. 升级依赖前调用 `get_changelog`，检查破坏性变更。

Agent 不得仅凭训练数据猜测组件 API，也不得因为 Blueprint 提供某个 Web 组件就绕过 Clipify 的平台抽象。例如，大型本地视频仍须通过 `IFilePicker` 或原生拖放选择，不能改用 `IBrowserFile` 上传。

## 无 MCP 时的回退

如果客户端不支持 MCP、MCP 启动失败或当前环境离线：

1. 从 [Blazor Blueprint LLM 文档索引](https://blazorblueprintui.com/llms/index.txt) 开始；
2. 按索引读取对应的纯文本安装、组件、Primitive、Pattern 或 Blueprint 文档；
3. 对照项目锁定的 NuGet 主版本，避免使用不匹配版本的 API；
4. 若仍无法确认 API，停止相关 UI 实现并记录待确认项，不要自行编造参数。

MCP 是首选的结构化、可查询入口，`llms/index.txt` 是静态回退和广泛阅读入口。不要把整套在线文档复制进仓库；离线开发优先使用 MCP 已生成的本地缓存。

## 提交要求

涉及 Blazor Blueprint 的提交应在说明中记录：

- 使用的 `BlazorBlueprint.Components` 版本；
- 查询过的组件或 Pattern；
- 是否通过 MCP 或 LLM 文档确认；
- 与官方示例不同的 Hybrid/PhotinoX 适配；
- 在 Windows WebView2、macOS WKWebView、Linux WebKitGTK 中完成的验证范围。
