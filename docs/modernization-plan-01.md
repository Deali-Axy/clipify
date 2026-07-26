# Clipify 现代化 · 第 1 轮（阶段 0 + 1）

> 状态：本地交付已提交；正式关闭待三平台 CI 跑绿  
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
| 基线 Tag | `baseline/pre-rewrite`（annotated，指向改写前 commit） |
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
| `global.json` | 最低 SDK `10.0.100` + `rollForward: latestFeature`（跟随 .NET 10 feature band，非 patch 死锁） |
| `Directory.Build.props` | Nullable、ImplicitUsings、`LangVersion=latest`、新项目 TreatWarningsAsErrors |
| `Directory.Packages.props` | 中央包版本管理 |
| `src/*` | Domain / Application / FFmpeg / Persistence / Hosting / UI / Desktop / Cli / Mcp 空骨架 |
| `tests/*` | 对应最小冒烟测试（assembly marker，无业务回归） |
| CI | 三平台基础 restore/build/test；强制 `bash` 以免 Windows PowerShell 掩盖失败 |
| 旧项目 | Core / Forms / Conveter 升级到 .NET 10；**保留** Maui 源码至阶段 2（已从活动解决方案移除） |

依赖方向遵循完整方案 [§5](./modernization-plan.md#5-依赖方向)。骨架只放维持引用关系所需的最小代码，不复制现有问题实现。

## 验收

- [x] 基线 Tag 存在且可复现旧代码（`baseline/pre-rewrite`）
- [x] 阶段成果已提交到 `modernization/phase-0-1`（可检出 / 可 PR）
- [x] 新 `src`/`tests` 在 .NET 10 下本地 restore / build / test 通过
- [x] 包版本由 `Directory.Packages.props` 集中管理
- [x] CI 工作流已添加，且 Windows 多命令步骤不会掩盖前序失败（`defaults.run.shell: bash`）
- [x] 未删除 MAUI 源码；未引入 PhotinoX / Blueprint / MCP SDK 等后续依赖
- [x] net10 升级后 WinForms 进程级启动冒烟通过（见下方）
- [ ] 远程三平台 CI 跑绿（需 push / PR 后确认，正式关闭本阶段的前置条件）

## 本轮结果摘要

### 修改摘要

- 新增本轮计划文档与中央构建/包管理配置
- 建立 `src/`、`tests/` 新架构空骨架（仅 marker / placeholder）
- 旧 Core / Forms 升级到 .NET 10，并接入中央包管理
- 增加三平台 CI（bash 退出码语义）；Maui 源码保留，已移出活动解决方案
- 按审阅意见修正 CI、归档 Tag 约定与 SDK 措辞

### 新增项目

`src`: Domain, Application, FFmpeg, Persistence, Hosting, UI, Desktop, Cli, Mcp  
`tests`: 对应 7 个冒烟测试项目

### 关键设计决定

1. **Maui 提前移出解决方案**：完整方案将删除放在阶段 2；本轮为使 `dotnet restore Clipify.sln` 在无 MAUI Workload 的机器上可用，仅从 `.sln` 移除，目录与 csproj 仍保留。
2. **`archive/maui-final` 指向 `baseline/pre-rewrite`**：阶段 1 之后 Core 已是 net10，而 Maui 仍为 net8 TFM 并引用 Core，归档树上不可构建。Tag 只作源码纪念，打在基线提交上（见完整方案 §2.6）。
3. **旧项目 warnings 不升格为错误**：`TreatWarningsAsErrors` 仅作用于 `src/`、`tests/`。
4. **SDK 策略是 feature band，不是精确锁定**：`global.json` 允许 roll-forward 到同 feature 的更新 patch（例如本机 `10.0.204`）；未启用 packages.lock.json。
5. **不引入后续阶段 NuGet**：无 PhotinoX、EF Core、System.CommandLine、MCP SDK、Blazor Blueprint。

### 验证命令与结果

```text
dotnet build Clipify.sln -c Release   # 成功；Forms 遗留 24 警告（可空性 + WindowsBase MSB3277）
dotnet test  Clipify.sln -c Release   # 7 个测试全部通过（marker 级）

# WinForms net10 进程冒烟（2026-07-27）
dotnet build Clipify.Forms/Clipify.Forms.csproj -c Release
Start-Process Clipify.Forms.exe → 存活 4s → Stop-Process
# 结果：SMOKE_OK（进程未立即退出；未覆盖裁剪/提音频功能路径）
```

### 已知问题

- Forms 升级 `net10.0-windows` 后出现 `WindowsBase` 版本冲突警告（MSB3277），不阻断构建与启动；阶段 9 归档前可不修。
- WinForms 冒烟仅为进程启动，尚未手工走完裁剪/提音频端到端。
- Maui 仍在仓库磁盘上，但不在活动解决方案中。
- 远程 CI 尚未验证（分支未推送时无法确认三平台）。

### 下一阶段前置条件

- push `modernization/phase-0-1`（或开 PR），确认 GitHub Actions 三平台成功
- 阶段 2：在 `baseline/pre-rewrite` 上创建 `archive/maui-final` → 删除 `Clipify.Maui` 目录与残留文档依赖

## 下一轮

单独执行 **阶段 2：删除 MAUI**。详见 [modernization-plan.md §15](./modernization-plan.md#阶段-2删除-maui) 与 [§2.6](./modernization-plan.md#26-删除-maui)。
