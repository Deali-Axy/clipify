# Clipify 现代化 · 第 3 轮（阶段 3：Domain、Application 与任务系统）

> 状态：已关闭（已合入 `modernization/trunk`；待合入 `master`）  
> 日期：2026-07-28  
> 完整方案：[modernization-plan.md](./modernization-plan.md)  
> 范围：仅阶段 3（见完整方案 [§15 阶段 3](./modernization-plan.md#阶段-3domainapplication-与任务系统)、[§6 任务系统](./modernization-plan.md#6-异步媒体任务系统)）  
> 前置：[modernization-plan-02.md](./modernization-plan-02.md) 已关闭  
> 分支：`modernization/phase-3-job-system`（已合入 `modernization/trunk`）

## 本轮目标

建立不依赖 FFmpeg 的可持久化异步任务内核，为后续 GUI、CLI、MCP 共用同一套任务生命周期打基础。

本轮实现 Domain Job 模型、Application 用例、EF Core SQLite 持久化、最小 Hosting Worker 和对应自动化测试。

## 不做

- 不实现 FFmpeg/ffprobe；
- 不迁移裁剪、音频提取或缩略图功能；
- 不接入 PhotinoX、Blazor Blueprint、CLI 命令或 MCP SDK；
- 不修改 Core / Forms / Conveter 的业务行为；
- 不引入 Dapper、EF Core InMemory、Hangfire、Redis、Daemon 或通用 DAG。

## 关键设计决定

1. SQLite 是任务状态的唯一事实来源；Channel 只负责有界唤醒。
2. Domain/Application 不引用 EF Core、SQLite、Hosting 或 UI。
3. 状态转换集中在 Domain；终态不可原地返回 Queued/Running。
4. 重试创建新 Job，并通过 `RetryOfJobId` 关联原任务。
5. Definition 使用白名单 discriminator + JSON，不保存任意 CLR 类型名。
6. Claim/Lease/Heartbeat 使用参数化原子 SQL，并校验 `LeaseOwner`。
7. Migration Lock 与 Job Lock 分离；过期租约且 Job Lock 已释放才可修复为 Interrupted。
8. 时间统一通过 `TimeProvider`；SQLite 可比较时间保存为 UTC Unix 毫秒。

### 本轮补充决定

- 阶段 3 仅注册白名单 Definition `fake_delay` 与对应 Fake Handler，真实媒体 Definition 留待阶段 4。
- 为规避 `Microsoft.Data.Sqlite` 10.0.10 传递依赖 `SQLitePCLRaw.lib.e_sqlite3` 的 NU1903（GHSA-2m69-gcr7-jv3q），直接固定 `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5。
- `dotnet-ef` 通过仓库本地 tool manifest（`.config/dotnet-tools.json`，10.0.10）管理，不依赖未记录的全局工具。
- `TransitionAsync` / `CancelAsync` 使用 `WHERE Id AND State=…` 条件 UPDATE，以受影响行数判定成功。
- `WatchAsync` 每订阅者独立 Channel；`PublishAsync` 在同一锁内 fan-out，保证跨订阅者顺序一致。

## 工作项

| 工作项 | 结果 |
|--------|------|
| Domain | `MediaJobId`、Definition、Snapshot、Progress、Artifact、State 与转换规则 |
| Application | `IMediaJobService`、Store/Queue/Handler 端口、取消、重试、历史、错误模型 |
| Persistence | `ClipifyDbContext`、Entity Mapping、Initial Migration、`IDbContextFactory` |
| 调度 | FIFO、并发限制、原子 Claim、Lease、Heartbeat、Busy 有界重试 |
| 恢复 | Queued 重载、过期租约修复、Interrupted 不自动重跑 |
| Hosting | 薄 `BackgroundService`，运行 Application 执行循环 |
| 测试 | Fake Handler、两个测试 Host 竞争、重启和锁测试 |

顺序为 Domain → Application → EF/Migration → Claim/Lease/Locks → Worker → 集成测试；每一步保持 build/test 通过。

## NuGet 与工具

版本统一写入 `Directory.Packages.props`：

- `Microsoft.EntityFrameworkCore.Sqlite`；
- `Microsoft.EntityFrameworkCore.Design`（`PrivateAssets=all`）；
- 必要时直接引用 `Microsoft.Data.Sqlite`；
- Hosting 所需的最小 Microsoft.Extensions 包；
- `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5（覆盖传递脆弱依赖）。

如使用 `dotnet-ef`，增加本地 tool manifest，并与 EF Core 10.x 版本保持一致。不得依赖未记录的全局工具。

## 测试重点

- Domain：状态转换、终态不可逆、时间范围、Progress 和 Retry 关联；
- Application：先持久化再唤醒、取消、重试、Handler 异常隔离；
- Persistence：Migration、映射、原子 Claim、Lease owner、Busy 重试；
- 集成：两个 Host 不重复执行同一 Job，并发上限 1/2 可靠；
- 恢复：Queued 不丢失，仍存活的其他 Host 任务不被误修复；
- 锁：租约过期但锁仍持有时不修复，锁释放后进入 Interrupted。

Persistence 测试使用真实 SQLite 临时文件；禁止用 EF Core InMemory 替代 SQLite 行为。

## 验收

- [x] 不依赖 FFmpeg 即可验证完整任务生命周期；
- [x] Domain 状态机有合法/非法转换测试；
- [x] EF Core Initial Migration 已提交；
- [x] `IDbContextFactory` 使用短生命周期 Context；
- [x] 排队、查询、取消、重试、历史和 Artifact 查询通过；
- [x] Claim/Lease/Heartbeat 使用参数化原子 SQL；
- [x] 两个测试 Host 不会重复执行同一 Job；
- [x] FIFO 与并发限制可靠；
- [x] 应用重启后状态正确，Interrupted 不自动重跑；
- [x] `src/`、`tests/` 无 `async void`；
- [x] 完整解决方案 restore/build/test 通过；
- [ ] Windows、macOS、Ubuntu CI 通过。（随合并 PR 验证）

## 验证命令

```bash
dotnet restore Clipify.sln
dotnet build Clipify.sln -c Release --no-restore
dotnet test Clipify.sln -c Release --no-build --no-restore
```

本地结果（2026-07-28）：Release build 成功（Forms 既有警告）；测试 **70** 通过；Codex 复审通过（4 项问题全部关闭）；并发压力测试额外复跑 5 轮通过。

### 审阅修复（已关闭）

1. Worker 循环仅使用 Queue 超时轮询，避免 `PeriodicTimer` 并发等待崩溃；
2. `CancelAsync` / `TransitionAsync` 均为条件 UPDATE，取消不会被 Succeeded 覆盖；
3. 并发计数包含全部 Running/Canceling（含租约已过期但仍持锁的任务）；
4. `WatchAsync` 独立 Channel + 同锁 fan-out；广播测试通过 `SubscriberCount` 握手等待注册。

## 本轮结果摘要

- Domain：Job 状态机、Definition 白名单序列化、Progress/Artifact/TimeRange
- Application：`MediaJobService` / `MediaJobExecutor` / Fake Handler / Channel 唤醒
- Persistence：EF Core SQLite、`InitialCreate` Migration、`EfMediaJobStore` 原子 Claim、File Job/Migration Lock、Busy 退避
- Hosting：`MediaJobWorker` + `AddClipifyMediaJobs`
- 测试覆盖双 Host、并发 2、重启恢复、锁语义与审阅竞态回归

## 下一轮

阶段 4：FFmpeg 基础设施。使用本阶段稳定的 Handler/Job Contract 接入 locator、runner、progress parser、ffprobe 和真实媒体任务。
