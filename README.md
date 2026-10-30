# WaferAoiUitest

主流程自动化回归的本机 Web 执行工具。本期覆盖原主流程 **01—07 节共 45 步**（01—06 节 27 步 + 07 节切割道两点角度校正 18 步）；其余 139 步不在本期。

- 需求：JCRJZDH-6（HiFox 任务 `c713889e-28b5-42a4-8e12-c689044f5e81`）
- 源工程：`D:\waferaoi\UiTests`（只迁移主流程必要依赖）
- 目标：独立目录、独立发布，不依赖原仓库源码、生产 sln 或测试 DLL
- 访问：仅本机回环 `127.0.0.1`，无账号体系、无局域网开放

## 目录与分工

```
WaferAoiUitest/
├─ backend/                 后端（卫青）
│  ├─ WaferAoi.UiTestTool.sln
│  ├─ src/WaferAoi.UiTestTool.Contracts   共享契约（netstandard2.0）
│  ├─ src/WaferAoi.UiTestTool.Web         Web 宿主（net8.0-windows）
│  └─ src/WaferAoi.UiTestTool.Executor    桌面执行器（net472 x64）
│     └─ Real/                            真实 UIA 执行（驱动/页面/断言/07 节）
├─ frontend/                前端（司小懿，独立项目）
├─ tools/build-release.ps1  本机发布脚本
├─ dist/                    发布产物（本机生成，不进版本库）
├─ docs/
│  ├─ api/                  OpenAPI、事件样例、契约答疑（后端唯一写入）
│  ├─ backend/              迁移清单、45 步能力表、真实运行说明
│  └─ design/               UI 设计交付（设计 Agent）
└─ .gitignore  .gitattributes  README.md
```

共享文件（根 README、`.gitignore`、`.gitattributes`）改动前先在任务里说一声；`docs/api/` 只由后端修改。

## 当前进度（2026-10-24）

**已完成并验证（本机）**

- 45 步静态目录：01—06 节 27 步沿用源工程编号/标题，07 节 07.01—07.18 按原用例新增；含章节、硬件/版本/人工标记与现场说明。
- **真实执行器已落地**（不再是阻塞占位）：
  - 01—06 由源工程逐文件迁移：`WaferAoiApplication`、`OwnedUi`、`SoftwareStartupFlow`、`RecipeEditorPage`、`NewRecipeDialog`、`WaferInformationPage`、断言与配方 XML 核对；
  - 07 节新实现：角度页导航、相机/倍率/光源/可见曝光增益、新转正与切割后、四参数、寸动步距与轴位读数、两点采集与 20mm 位移核验、保存、采图调试、运行成功提示、角度结果读取；
  - 事件、机器锁、幂等、历史、待现场处理全部保留；人工点改走网页确认点。
- 真实/模拟双入口：真实模式启动被测程序并按事实断言；模拟模式仅联调，报告与快照带 `simulated=true`。
- 编译：Contracts / Web / Executor Release 0 错误（VS 2022 MSBuild）。
- 模拟端到端（本机）：45 步；跳过硬件策略 `passedWithSkips`（23/22/0/0）；严格策略 5 个确认点（03.01、03.02、06.01、07.10、07.11）逐个提交后 `passedWithSkips`（44 含 5 人工 / 1 跳过）；报告 45 步、证据下载 200。
- 真实入口冒烟：真实模式创建运行后执行器被实际启动；未提权/无密码时 01.01 记 `blocked`、其余 44 步 `notRun`、结论 `blocked`、清理 `succeeded`，没有伪造通过。
- 待现场处理闭环（2026-10-30 模拟复验）：执行器在确认点被杀后，当前步骤记 `unknown`、结论 `pendingField`、锁保持；宿主重启仍为待现场处理并拒绝新一轮；现场处理核验通过后才删除机器锁，运行阶段收敛为 `finished`、结论仍是 `interrupted`（不回写成功、不自动续跑）。被测程序 PID 在启动后立即写入 `product-process.json`，执行器崩溃后仍可用于核验。
- 2026-10-27 现场真实运行：走到 07.01 时因“参数输入框未被唯一识别”阻塞（旧版阻塞文案，页面本身有四个参数），其后进入待现场处理并保留机器占用。该轮使用的执行器不是 HEAD 发布包；当前 `dist` 已按 HEAD 重建（含 07.01 定位修复 9f7cf2c），下次现场运行请从新的 `dist` 启动。

**未完成（不得当作已交付）**

- 实机 01—07 真实执行：需要管理员会话、现场密码、设备与固定测试片；07 页无 AutomationId 控件的运行时定位唯一性须现场首跑逐条确认。
- 02.02/06.04 的“完成状态”可读来源、07.10/07.11 的图像内容判据：严格策略读不到即阻塞（详见 `docs/backend/step-capability.md` 缺口表）。
- 前端仍把范围徽标硬编码为“本期 27 步 / 完整 184 步”（`frontend/src/App.tsx`），需前端改文案；接口返回的步骤与计数已是 45。

## 一键运行（真实模式）

```bat
cd /d D:\WaferAoiUitest\dist
set WAFER_AOI_UI_PASSWORD=<现场 admin 密码>
start-real.cmd
```

地址 `http://127.0.0.1:5180`。宿主必须以管理员身份启动。完整前置条件、配置项、验证步骤与证据路径见 **`docs/backend/real-run-guide.md`**。

模拟模式：`start-simulate.cmd`（5181）。

## 构建与发布

本机对源码文件有透明加密/过滤行为：`csc` 读取 `.cs` 会被当成二进制（CS2015），`dotnet` 也无法写入受保护目录。已验证可行的方式（`tools/build-release.ps1` 已封装）：

1. 把 `backend` 同步到可写镜像 `%USERPROFILE%\Documents\deepseek-harness\default-workspace\mirror\backend`；
2. 用 **Visual Studio 2022 的 MSBuild**（已获授权，可正常读取 `.cs`）还原并构建：
   `"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" <sln> /restore /p:Configuration=Release`
3. 发布 Web 与执行器，合并 `frontend/dist`，复制到 `dist`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\WaferAoiUitest\tools\build-release.ps1
```

## 数据目录

默认 `%LOCALAPPDATA%\WaferAoiUiTestTool`（可用 `--data-dir` 指定）：

```
config.json              本机配置（无密码）
lock.json                机器占用 / 待现场处理（跨宿主重启保留）
runs/<runId>/
  manifest.json          本轮选项与配置快照（45 步）
  events.jsonl           唯一有序事件流
  report.json            最终报告
  artifacts/             证据（截图、steps.log、主流程步骤结果.txt、失败快照）
  commands/  acks/       网页确认命令与执行器回执
  product-process.json   执行器启动被测程序后立即落盘的身份（PID + 启动时间）
  executor.stdout.log   执行器诊断输出（非协议流）
```

## 选项语义

| 跳过版本断言 | 硬件策略 | 行为 |
| --- | --- | --- |
| 否 | strict（两项都不选） | 严格按原步骤；01.02 核验版本与编译日期；硬件点自动执行 |
| 是 | strict | 只跳过版本；01.02 记 `userSkipped` |
| 否/是 | skip | 硬件步骤/步骤内硬件部分记跳过；混合步骤仍跑软件断言 |
| 否/是 | manual | 硬件点等待网页确认；完成后仍执行软件断言（**实机建议**） |

后端只校验两个硬件选项互斥；请求里同时为真返回 400 `invalid_options`，不创建运行、不启动进程。

## 文档索引

- `docs/backend/real-run-guide.md`：交付包、启动、配置、前置条件、验证与缺口
- `docs/backend/step-capability.md`：45 步能力表（含 07 实现方式与策略分支）
- `docs/backend/migration-inventory.md`：源文件归属、已解除耦合、迁移与新增清单
- `docs/api/openapi.yaml`：完整 HTTP/SSE 契约
- `docs/api/events-sample.jsonl`：事件样例（可直接回放）
- `docs/api/contract-decisions.md`：与前端确认的口径、部署与 mock
