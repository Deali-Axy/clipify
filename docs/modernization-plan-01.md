# Clipify 现代化 · 第 1 轮（阶段 0 + 1）

> 状态：阶段 0 + 1 已完成（本地验证通过）  
> 日期：2026-07-26  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 范围：仅阶段 0、阶段 1（见完整方案 §15、§20）
> 分支：`modernization/phase-0-1`  
> 基线 Tag：`baseline/pre-rewrite`

## 本轮目标

建立可回滚的安全基线，并搭好 .NET 10 解决方案骨架，为后续 Domain / FFmpeg / CLI / UI 迁移做准备。

**不做：** 删除 MAUI、迁移 UI、实现 FFmpeg/CLI/MCP、引入 PhotinoX / Blazor Blueprint。

## 阶段 0：安全基线

对应完整方案 [§15 阶段 0](./modernization-plan.md#阶段-0建立安全基线)。

| 项 | 说明 |
|----|------|
| 分支 | `modernization/phase-0-1` |
| 基线 Tag | `baseline/pre-rewrite` |
| 构建基线 | 见下方「构建基线记录」 |
| 功能基线 | 见下方「当前功能与缺陷」 |

### 构建基线记录

在打 Tag `baseline/pre-rewrite`（commit `9019d19`）时，本机 SDK `10.0.204`，`Release` 配置：

| 项目 | 结果 |
|------|------|
| `Clipify.Core`（net8.0） | 成功，0 警告，0 错误 |
| `Clipify.Forms`（net8.0-windows） | 成功，23 个可空性警告，0 错误 |
| `ClipifyConveter`（net10.0） | 成功，0 警告，0 错误 |
| `Clipify.Maui` | 本轮未作为基线构建（阶段 2 归档删除） |

### 当前功能与缺陷（WinForms 参考）

可用功能：

- 视频裁剪（`VideoSplit`）
- 音频提取（`ExtractAudio`）
- 文件/目录选择（MediatR + WinForms 对话框）
- 导出弹窗与 FFmpeg 进度展示（`VideoExportDialog`）

已知问题（完整方案 [§3](./modernization-plan.md#3-当前代码的主要问题)）：

- UI 与 FFmpeg 强耦合；存在 `async void` 事件处理
- Core 依赖 `xFFmpeg.NET`，并耦合 UI 消息接口
- `SplitVideoAsync` 生成参数与实际执行不一致；进度回调未消费
- 任务无独立生命周期（排队、历史、取消、崩溃恢复）

手工验收（最小）：构建并启动 Forms → 选视频 → 裁剪/提音频 → 确认导出。截图仍保留在 `docs/_images/`。

## 阶段 1：解决方案与 .NET 10 基础

对应完整方案 [§15 阶段 1](./modernization-plan.md#阶段-1解决方案与-net-10-基础)、[§4 目标结构](./modernization-plan.md#4-目标解决方案结构)。

| 交付物 | 说明 |
|--------|------|
| `global.json` | 锁定 SDK（.NET 10） |
| `Directory.Build.props` | Nullable、ImplicitUsings、统一 TFM 等 |
| `Directory.Packages.props` | 中央包版本管理 |
| `src/*` | Domain / Application / FFmpeg / Persistence / Hosting / UI / Desktop / Cli / Mcp 空骨架 |
| `tests/*` | 对应最小测试项目（至少 Domain 冒烟测试） |
| CI | 三平台基础 restore/build/test |
| 旧项目 | Core / Forms / Conveter 升级到 .NET 10；**保留** Maui 源码至阶段 2（已从活动解决方案移除，避免无 Workload 时阻断 restore） |

依赖方向遵循完整方案 [§5](./modernization-plan.md#5-依赖方向)。骨架只放维持引用关系所需的最小代码，不复制现有问题实现。

## 验收

- [x] 基线 Tag 存在且可复现旧代码（`baseline/pre-rewrite`）
- [x] 新 `src`/`tests` 在 .NET 10 下 restore / build / test 通过
- [x] 包版本由 `Directory.Packages.props` 集中管理
- [x] CI 工作流已添加（`.github/workflows/ci.yml`，Windows / macOS / Linux）
- [x] 未删除 MAUI 源码；未引入 PhotinoX / Blueprint / MCP SDK 等后续依赖

## 本轮结果摘要

### 修改摘要

- 新增本轮计划文档与中央构建/包管理配置
- 建立 `src/`、`tests/` 新架构空骨架（仅 marker / placeholder）
- 旧 Core / Forms 升级到 .NET 10，并接入中央包管理
- 增加三平台 CI；Maui 源码保留，但已从活动解决方案移除（无 Workload 会阻断 restore）

### 新增项目

`src`: Domain, Application, FFmpeg, Persistence, Hosting, UI, Desktop, Cli, Mcp  
`tests`: 对应 7 个冒烟测试项目

### 关键设计决定

1. **Maui 提前移出解决方案**：完整方案将删除放在阶段 2；本轮为使 `dotnet restore Clipify.sln` 在无 MAUI Workload 的机器上可用，仅从 `.sln` 移除，目录与 csproj 仍保留至阶段 2 打 `archive/maui-final` 后再删。
2. **旧项目 warnings 不升格为错误**：`TreatWarningsAsErrors` 仅作用于 `src/`、`tests/`。
3. **不引入后续阶段 NuGet**：无 PhotinoX、EF Core、System.CommandLine、MCP SDK、Blazor Blueprint。

### 验证命令与结果

```text
dotnet build Clipify.sln -c Release   # 成功；Forms 遗留 24 警告（可空性 + WindowsBase MSB3277）
dotnet test  Clipify.sln -c Release   # 7 个测试全部通过
```

### 已知问题

- Forms 升级 `net10.0-windows` 后出现 `WindowsBase` 版本冲突警告（MSB3277），不阻断构建；阶段 9 归档前可不修。
- Maui 仍在仓库磁盘上，但不在活动解决方案中。

### 下一阶段前置条件

- 本分支可提交 / PR（由维护者决定）
- 阶段 2：创建 `archive/maui-final` Tag → 删除 `Clipify.Maui` 目录与残留文档依赖

## 下一轮

单独执行 **阶段 2：删除 MAUI**（先打 `archive/maui-final` Tag）。详见 [modernization-plan.md §15](./modernization-plan.md#阶段-2删除-maui)。
