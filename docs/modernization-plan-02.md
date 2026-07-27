# Clipify 现代化 · 第 2 轮（阶段 2：删除 MAUI）

> 状态：已关闭（本地验收通过；提交 `aff4f48` / `cee72cf`）  
> 日期：2026-07-27  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 范围：仅阶段 2（见完整方案 [§15 阶段 2](./modernization-plan.md#阶段-2删除-maui)、[§2.6](./modernization-plan.md#26-删除-maui)）  
> 前置：[modernization-plan-01.md](./modernization-plan-01.md) 已关闭  
> 分支：`modernization/phase-0-1`  
> 归档 Tag：`archive/maui-final` → `9019d19`（与 `baseline/pre-rewrite` 同 commit）

## 本轮目标

在远端归档点已建立的前提下，从活动仓库彻底移除 `Clipify.Maui`，使解决方案不再依赖 MAUI Workload。

**不做：** Domain/Job 实现、FFmpeg 重写、CLI/MCP、PhotinoX / Blazor Blueprint、删除 WinForms。

## 前置条件（已完成）

| 项 | 状态 |
|----|------|
| 阶段 0+1 技术验收 | 通过（见 [plan-01](./modernization-plan-01.md)） |
| 三平台 CI | `ee4cfa4` success |
| `baseline/pre-rewrite` 推远端 | 已 peel 到 `9019d19` |
| `archive/maui-final` 推远端 | annotated，直接指向 commit `9019d19`（非嵌套 tag） |

创建约定（避免 annotated tag 指向另一个 annotated tag）：

```bash
git tag -a archive/maui-final 9019d19fd44b9b37053f00c731e5e9af31e53eda -m "Archive MAUI before removal"
git push github refs/tags/baseline/pre-rewrite refs/tags/archive/maui-final
```

## 工作项

对应完整方案 [§15 阶段 2](./modernization-plan.md#阶段-2删除-maui)：

1. 确认活动 `Clipify.sln` 不含 MAUI（阶段 1 已移出）。
2. 删除 `Clipify.Maui/` 目录。
3. 清理只服务于 MAUI 的残留（如 `.gitignore` 中的 Maui 路径、文档中的运行说明）。
4. `dotnet build` / `dotnet test` 确认新旧桌面相关项目仍可构建。

## 验收

- [x] 解决方案不包含 MAUI（`.sln` 无 Maui 条目）
- [x] 仓库不要求安装 MAUI Workload
- [x] `Clipify.Maui` 目录已删除；历史可通过 `archive/maui-final` / `baseline/pre-rewrite` 检出
- [x] `dotnet build Clipify.sln -c Release` 与 `dotnet test Clipify.sln -c Release` 通过（7 tests）

## 本轮结果摘要

- 已推远端 Tag：`baseline/pre-rewrite`、`archive/maui-final`（均 peel 到 `9019d19`）
- 已回填 [modernization-plan-01.md](./modernization-plan-01.md) 为「已关闭」
- 已 `git rm -r Clipify.Maui`，并清理 `.gitignore` 中 Maui 路径
- 本地 `Release` build/test 通过（Forms 仍有既有 MSB3277 警告）

## 回滚

```bash
git checkout archive/maui-final -- Clipify.Maui
# 或整树：git switch --detach archive/maui-final
```

## 下一轮

阶段 3：Domain、Application 与任务系统。详见 [modernization-plan.md §15](./modernization-plan.md#阶段-3domainapplication-与任务系统)。
