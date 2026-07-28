# Clipify 现代化 · 第 6 轮（阶段 6：MCP Server）

> 状态：待实施  
> 日期：2026-07-28  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 前置：[modernization-plan-05.md](./modernization-plan-05.md) 已关闭  
> 工作分支：`modernization/phase-6-mcp`

## 本轮目标

创建 `Clipify.Mcp`，以 stdio 暴露媒体与任务工具，复用 Hosting 和 Application。

## 边界

实现 SDK、Schema、风险注解、Job 等待、allow-root 和配置示例；禁止 Shell、URL、原始参数及越界路径。

## 验收清单

- [ ] stdout 仅含 JSON-RPC；
- [ ] 工具可发现、可调用；
- [ ] 路径逃逸被阻止；
- [ ] 长任务及时返回 JobId；
- [ ] 测试及三平台构建通过。

## 下一轮

阶段 7：PhotinoX Shell。
