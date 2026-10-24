# 迁移清单与依赖收敛（T01 初版）

源工程：`D:\waferaoi\UiTests`（net472，FlaUI UIA3 4.0.0）。
目标：`D:\WaferAoiUitest\backend`，两个入口（Web 宿主 net8.0 + 桌面执行器 net472）。
范围：01—07 节 45 步（01—06 节 27 步 + 07 节 18 步）；其余 139 步不迁移。

## 0. 本阶段更新（2026-10-24）

本文正文第 1—5 节记录 T01 平台层初版（01—06 节 27 步）。2026-10-24 已完成 01—07 节 45 步的真实执行迁移与发布，
逐项清单见第 6 节；第 1 节表中「待迁移」状态以第 6 节为准。

## 1. 文件归属

| 源文件 | 目标位置 | 处理方式 | 当前状态 |
| --- | --- | --- | --- |
| `Tests/MainFlow/Infrastructure/MainFlowStepPlan.cs` | `Contracts/RunModels.cs`（`MainFlowCatalog`） | 27 步编号/标题原样保留，补章节、硬件/版本/人工标记 | 已完成 |
| `Tests/MainFlow/Infrastructure/MainFlowInputs.cs` | `Executor/Inputs/MainFlowInputs.cs` | 固定输入原值迁移，禁止改算法参数 | 待迁移 |
| `Tests/MainFlow/Infrastructure/StepRecorder.cs` | `Executor/Events/StepEngine.cs` + `EventWriter.cs` | 保留状态语义；开始/等待/终态改为结构化事件 | 事件出口已完成，步骤引擎待迁移 |
| `Tests/MainFlow/MainFlowTests.cs` | `Executor/Flows/MainFlowRunner.cs` | 去掉 MSTest 生命周期与 `TestContext`，改由执行器入口调用 | 待迁移 |
| `Tests/MainFlow/Sections/*.cs` | `Executor/Flows/Sections/*.cs` | 保持章节顺序与编号；人工确认改网页命令 | 待迁移 |
| `Drivers/WaferAoiApplication.cs` | `Executor/Drivers/` | 保留 PID 归属、启动与前置检查 | 待迁移 |
| `Drivers/OwnedUi.cs`、`SoftwareUi.cs`、`UiWait.cs` | `Executor/Drivers/` | 保留唯一定位、失焦提交、等待语义 | 待迁移 |
| `Drivers/ManualCheckpoint.cs` | `Executor/Checkpoints/CheckpointGate.cs` + `CommandChannel.cs` | 由 WinForms 弹窗改为网页确认 + 命令文件 + 回执 | 已完成（协议层） |
| `Windows/SoftwareStartupFlow.cs`、`LoginWindow.cs`、`MainWindow.cs` | `Executor/Windows/` | 只迁移启动、登录、主窗口定位 | 待迁移 |
| `Pages/NewRecipeDialog.cs`、`RecipeEditorPage.cs` | `Executor/Pages/` | 仅主流程调用闭包 | 待迁移 |
| `Pages/WaferInformationPage*.cs`（partial） | `Executor/Pages/` | 四个分部类一起迁移；先核私有成员依赖 | 待迁移 |
| `Assertions/WaferInformationAssertions.cs` | `Executor/Assertions/` | 保留断言语义；允许继续用 TestFramework 的 Assert | 待迁移 |
| `TestData/*.cs` | `Executor/TestData/` | 去掉源码目录推断；保留配方资源校验 | 待迁移 |
| `Diagnostics/ArtifactCollector.cs` | `Executor/Diagnostics/` + Web 证据索引 | 拆出 TestContext；证据登记改为 `artifactCreated` | 部分完成（登记协议已完成） |
| `Fixtures/WaferAoiTestBase.cs` | `Executor/Lifecycle/ExecutorLifecycle.cs` | 初始化/失败采集/清理改为执行器自己的生命周期 | 部分完成（Web 侧生命周期、锁、待现场处理已完成） |
| `Configuration/UiTestSettings.cs` | `Contracts/ConfigModels.cs` + `Web/Storage/ConfigStore.cs` | 显式配置，删除生产 sln 查找；版本跳过与单一硬件策略 | 已完成（不含密码落盘） |
| `Compatibility/*`、`FlaUiTypeAliases.cs` | `Executor/Compatibility/` | 按实际编译引用保留 | 待迁移 |
| MSTest 包 | 执行器保留 TestFramework | 不依赖测试发现、TestAdapter、TestContext 或 VS | 已按此约束搭建 |

## 2. 已解除的耦合（后端平台层）

- **不再依赖 `TestContext`**：Web 与执行器的状态、日志、证据都走事件与文件；报告由事件投影生成。
- **不再查找生产 sln**：配置全部显式保存，路径缺失直接在校验中报错。
- **人工确认不再用 WinForms**：`checkpointId + runId + requestId` 三元约束，命令文件原子写入，执行器回执。
- **清理策略改为保留程序**：硬件状态未确认时不 Kill，锁与待现场处理写入 `lock.json`，宿主重启仍占用。
- **单实例**：`lock.json` 原子创建；创建运行、执行、清理、待处理全过程持有，不因页面断开或步骤结束释放。

## 3. 仍需处理的依赖（T04—T05 的真实工作量）

1. `WaferInformationPage` 是 4 个 partial 文件，含软件断言与专项用例方法；只取主流程闭包，不能整目录搬迁。
2. `OwnedUi` 依赖 `UiTestSettings` 的多个超时与路径字段；迁移时先定配置映射，避免行为漂移。
3. `MainFlowTests` 的版本核验在登录后执行（01.02 延后），事件顺序必须保留这一事实，不能按编号重排。
4. `ArtifactCollector` 的截图/UIA 树/进程状态采集方法可原样复用，但 `TestContext.AddResultFile` 改为事件登记。
5. 严格路径缺少可观察状态的步骤（03.02、06.01、06.04、06.03、02.02、04.01）必须先补观察依据，见 `step-capability.md`。

## 4. 本切片实际交付

- 契约：`Contracts`（27 步、选项、事件、快照、报告、配置）。
- Web：配置读写与校验、创建/幂等/单实例、SSE 补流、确认命令、证据接口、历史与报告、待现场处理核验。
- 执行器：事件写入、命令通道、确认等待、模拟流程（契约联调）、真实流程入口（当前显式阻塞）。
- 文档：OpenAPI、事件样例、契约答疑、能力表、本清单。

## 5. 验证记录与未做项

**已完成验证（2026-10-23）**

- 编译：Contracts / Web / Executor 三个项目 0 警告 0 错误（在本机 `D:\waferaoi` 下的逐文件临时副本中完成，校验后已删除）。
- 模拟端到端（`--simulate`，不启动被测程序）：
  - strict：03.01、03.02、06.01 三个网页确认点；最终 26 步成功、3 步人工确认、1 步跳过，结论 `passedWithSkips`；
  - fail：04.03 失败即停，04.04 起保持 `notRun`，结论 `failed`，清理 `succeeded`；
  - pending-field：进入待现场处理，新运行返回 409；提交现场处理并通过核验后解除占用，原结论保持 `pendingField`；
  - SSE：`sequence` 1..N 无缺口；证据按 `artifactId` 取回 200。
- 模拟结果只证明协议与页面分支，不代表实机通过。

**未做与原因**

- 27 步真实 UIA 迁移与严格能力补齐（T04—T05）未完成：需要连续编译与实机窗口；迁移后的代码必须实机验证，不能以未验证代码冒充完成。
- 未实际启动被测程序、未操作设备：按用户要求默认不启动。
- 已上传 GitHub：分支 `feat/backend-JCRJZDH-6-backend-platform`，远端 HEAD 与本地一致（`5a8cb19`）。
- 目标目录存在透明加密限制：目录内直接构建会出现 CS2015。源码可用 `rg` 检索；构建在 `D:\waferaoi` 下的临时镜像完成，不需要白名单。
## 6. 01—07 节 45 步真实执行迁移（2026-10-24）

### 6.1 迁移复用（01—06 节，逐文件）

| 源文件 | 目标文件 | 处理 |
| --- | --- | --- |
| `Drivers/WaferAoiApplication.cs` | `Executor/Real/WaferAoiApplication.cs` | 保留 PID 归属、启动与窗口枚举；去掉前置检查中的测试框架依赖 |
| `Drivers/OwnedUi.cs`、`SoftwareUi.cs`、`UiWait.cs` | `Executor/Real/OwnedUi.cs`、`UiWait.cs` | 唯一定位/进程归属/失焦提交/有界等待原样保留；`Assert.Inconclusive` 改为 `StepVerdictException(Blocked)` |
| `Windows/SoftwareStartupFlow.cs` | `Executor/Real/SoftwareStartupFlow.cs` | 登录、回零提示、进入配方页；新增“确认回零并读取回零状态”分支 |
| `Pages/RecipeEditorPage.cs`、`NewRecipeDialog.cs` | `Executor/Real/RecipePages.cs` | 只保留主流程调用闭包（搜索、打开、新建、当前配方名、保存） |
| `Pages/WaferInformationPage*.cs` | `Executor/Real/WaferInformationPage.cs` | 合并两份 partial 的主流程部分；断言改 Verify |
| `Tests/MainFlow/Sections/*.cs` | `Executor/Real/MainFlowRunner.*.cs` | 按章节拆为 Startup/Recipe/WaferInformation/WaferToStage/StreetAlignment |
| `Tests/MainFlow/Infrastructure/MainFlowInputs.cs` | `Executor/Real/MainFlowInputs.cs` | 固定输入原值，追加 07 节固定值 |
| `Tests/MainFlow/Infrastructure/StepRecorder.cs` | `Executor/Real/StepRecord.cs` + `MainFlowRunner` | 状态语义保留（只减弱不增强）；出口改为结构化事件 |
| `Diagnostics/ArtifactCollector.cs` | `Executor/Real/ExecutorArtifacts.cs` | 截图/UIA 树/进程状态/失败采集；`AddResultFile` 改为 `ArtifactCreated` |
| `Configuration/UiTestSettings.cs` | `Executor/Real/ExecutorSettings.cs` | 只从本轮 manifest 快照取配置；密码仅读环境变量 |
| `Drivers/ManualCheckpoint.cs`（WinForms 弹窗） | `CheckpointGate` + `StepContext.Confirm` | 改为网页确认点，完成/阻塞/失败三态 |
| `TestData/*.cs` | `Executor/Real/TestData.cs` | 只保留主流程用到的选项值/时间字段/配方产物描述 |

### 6.2 本次新实现（07 节及能力补齐）

| 目标 | 说明 |
| --- | --- |
| `Executor/Real/StreetAlignmentPage.cs` | 07 页全部实际操作：导航、相机、倍率+设置、明场/红外/暗场、可见曝光增益、新转正/切割后、四参数、连续采集/采单张、寸动步距与方向按钮、点位行/增加/移动、保存/运行、角度结果、图像区亮度统计 |
| `Executor/Real/NamedUi.cs` | 无 AutomationId 控件的结构化定位：名称 + 控件类型 + 同行几何归属；歧义即返回 null（调用方阻塞），不猜坐标 |
| `Executor/Real/MainFlowRunner.StreetAlignment.cs` | 07.01—07.18 的步骤编排、位移/同 Y/4 位小数/运行成功等断言 |
| `MainFlowRunner` 02.02 / 06.03 / 06.04 | 严格策略自动确认回零、自动点击一键上片并处理介质/物料提示、读完成状态；读不到状态即阻塞 |
| `Executor/Real/Verify.cs`、`StepRecord.cs`、`StringCompat.cs`、`IsExternalInit.cs` | 断言语义、步骤上下文、net472 兼容 |
| `Contracts/RunModels.cs` | 目录扩到 45 步（新增 07 节 18 步与人工/硬件/观察字段），计数分母随常量变化 |

### 6.3 平台层改动

- `MainFlowCatalog.ImplementedStepCount` 27 → 45；新增章节标题“切割道两点角度校正”。
- 模拟流程文案、报告备注、`Enums` 注释同步为 45 步 / 其余 139 步。
- `Executor.csproj` 关闭 WinForms（人工确认已全部改网页）；执行器仍需 net472 x64。
- 新增 `tools/build-release.ps1` 与 `dist` 发布包（`dist` 不进版本库）。

### 6.4 构建与验证

- 构建：Contracts / Web / Executor Release 均 0 错误；使用 VS 2022 的 MSBuild（本机 csc 对受保护目录无读权限，详见 README“构建与发布”）。
- 模拟端到端：45 步；跳过硬件 23/22/0/0；严格 5 个确认点（03.01、03.02、06.01、07.10、07.11）全部提交后 44 通过（含 5 人工）/1 跳过；报告 45 步；证据下载 200。
- 真实入口：真实模式执行器被实际启动；未提权/无密码 → 01.01 `blocked`、其余 `notRun`、结论 `blocked`、清理 `succeeded`，无伪造通过。
- **未做**：实机 01—07 运行（需管理员、现场密码、设备与固定测试片）；07 页运行时定位唯一性与轴位读数格式待现场首跑确认。