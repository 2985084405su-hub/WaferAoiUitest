# WaferAoiUitest

主流程自动化回归的本机 Web 执行工具。本期覆盖原主流程 01—06 节共 27 步；其余 157 步不在本期。

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
│  └─ src/WaferAoi.UiTestTool.Executor    桌面执行器（net472）
├─ frontend/                前端（司小懿，独立项目）
├─ docs/
│  ├─ api/                  OpenAPI、事件样例、契约答疑（后端唯一写入）
│  ├─ backend/              迁移清单、27 步能力表
│  └─ design/               UI 设计交付（设计 Agent）
├─ .gitignore  .gitattributes  README.md
```

共享文件（根 README、`.gitignore`、`.gitattributes`）改动前先在任务里说一声；`docs/api/` 只由后端修改。

## 当前进度（2026-10-23）

**已完成并验证**

- 27 步静态目录，编号/标题与 `MainFlowStepPlan` 一致；补章节、硬件/版本/人工标记。
- 本机配置读写：显式路径、版本可选必填、单一硬件策略；密码只从 `WAFER_AOI_UI_PASSWORD` 读取，不回传、不落盘。
- 单实例机器锁（原子创建）与待现场处理状态持久化；宿主重启不释放占用。
- 创建运行幂等、运行快照、27 步计数、SSE 补流、确认点命令与回执、证据索引/文件接口、历史与报告。
- 失败/阻塞即停、后续保持未执行；执行器异常退出标记当前步“结果未知”并进入待现场处理。
- 执行器事件 JSONL、命令通道、确认等待、模拟流程（六种选项组合/失败/待处理）。
- `docs/api/openapi.yaml`、事件样例、10 项契约答疑。
- 编译通过：Contracts / Web / Executor 三个项目 0 警告 0 错误（校验副本）。
- 模拟端到端冒烟通过：严格策略 3 个网页确认点、失败即停、待现场处理与新轮 409、现场处理解除占用、SSE `sequence` 1..N 无缺口。

**未完成（不得当作已交付）**

- 27 步真实 UIA 迁移与严格能力补齐（T04—T05）；当前 `LiveFlow` 显式阻塞，不启动被测程序。
- 独立发布包与实机联调；未启动被测程序、未操作设备。
- 上传 GitHub：本机无可用凭据，`git ls-remote` 被要求用户名；仓库 API 对匿名访问返回 404。

## 本机环境限制（会影响构建）

本机对 `D:\WaferAoiUitest` 下的新建文件有透明加密/过滤行为：PowerShell 读取为正常文本，但 `csc` 会把这些文件当作二进制（CS2015）或读到密文。`D:\waferaoi` 下同样会在数分钟后出现该现象。

已验证可行的构建方式：

1. 用逐文件 `Copy-Item`（不要 `robocopy` 整目录）复制到 `D:\waferaoi` 下的临时目录；
2. 立即执行 `dotnet build -p:UseSharedCompilation=false`；
3. 校验完成后删除临时目录，不修改源仓库文件。

这不是代码问题，但会导致在目标目录内直接构建不可靠。建议由测试机管理员把 `D:\WaferAoiUitest` 加入加密软件白名单，或固定在 `C:\Build` 等受控目录构建。

## 本机运行（开发期）

前置：Windows、.NET 8 SDK（Web）、.NET Framework 4.7.2（执行器）、管理员会话（UIA 与被测程序要求）。

```powershell
# 1. 构建（需要用户明确的编译窗口）
dotnet build backend\WaferAoi.UiTestTool.sln -c Release

# 2. 启动 Web，指向前端构建产物与执行器
dotnet run --project backend\src\WaferAoi.UiTestTool.Web -- `
  --port 5180 `
  --data-dir "$env:LOCALAPPDATA\WaferAoiUiTestTool" `
  --web-root "D:\WaferAoiUitest\frontend\dist" `
  --executor "D:\WaferAoiUitest\backend\src\WaferAoi.UiTestTool.Executor\bin\x64\Release\net472\WaferAoi.UiTestTool.Executor.exe"

# 3. 契约联调（模拟执行器，不代表实机通过）
dotnet run --project backend\src\WaferAoi.UiTestTool.Web -- --simulate --scenario manual
```

数据目录默认 `%LOCALAPPDATA%\WaferAoiUiTestTool`：

```
config.json              本机配置（无密码）
lock.json                机器占用 / 待现场处理（跨宿主重启保留）
runs/<runId>/
  manifest.json          本轮选项与配置快照
  events.jsonl           唯一有序事件流
  report.json            最终报告
  artifacts/             证据
  commands/  acks/       网页确认命令与执行器回执
  executor.stdout.log   执行器诊断输出（非协议流）
```

## 选项语义

| 跳过版本断言 | 硬件策略 | 行为 |
| --- | --- | --- |
| 否 | strict（两项都不选） | 严格按原步骤；01.02 核验版本与编译日期 |
| 是 | strict | 只跳过版本；01.02 记 `userSkipped` |
| 否/是 | skip | 硬件步骤/步骤内硬件部分记跳过；混合步骤仍跑软件断言 |
| 否/是 | manual | 硬件点等待网页确认；完成后仍执行软件断言 |

后端只校验两个硬件选项互斥；请求里同时为真返回 400 `invalid_options`，不创建运行、不启动进程。

## 文档索引

- `docs/api/openapi.yaml`：完整 HTTP/SSE 契约
- `docs/api/events-sample.jsonl`：事件样例（可直接回放）
- `docs/api/contract-decisions.md`：与前端确认的 10 项口径、部署与 mock
- `docs/backend/migration-inventory.md`：源文件归属、已解除耦合、未做项
- `docs/backend/step-capability.md`：27 步能力表与人工点
