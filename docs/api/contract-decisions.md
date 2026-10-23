# 前后端接口确认（后端答疑，JCRJZDH-6）

对应司小懿前置核对第 7 节的 10 个待确认点。**正式契约以 `docs/api/openapi.yaml` 为准**；本文解释取舍和联调方式。
协议版本：`1`。状态：这些口径已在后端实现，前端可按此落类型。

## 1. 27 步清单来源与字段

清单由后端下发，前端不要内置状态：

- `GET /api/catalog/steps`：静态定义，含 `stepId / title / section / sectionTitle / hardwareRelated / versionAssertion / manualRequirement / manualInstruction / observation`。
- 运行快照 `steps[]`：运行态，含 `status / reason / startedAt / finishedAt / durationMs / manual / hardwareSkipped / evidenceIds / notes`。

`manualRequirement` 三态：`none`、`always`（原用例明确要求现场物理操作，如 03.01）、`hardwarePolicyOnly`（仅人工硬件策略下等待，如 06.03）。
01.02 保留在清单中；跳过版本时状态为 `userSkipped`，原因是“本轮选择跳过版本断言（版本号及编译日期）”。
编号不保证等于真实执行顺序（01.02 在登录后发生），页面按 section 分组、按真实事件展示。

## 2. 启动幂等与机器忙

`POST /api/runs`：

| 场景 | 状态码 | body |
| --- | --- | --- |
| 新建成功 | 201 | `{ "created": true, "run": RunSnapshot }` |
| 同一 `requestId` 重复提交 | 200 | `{ "created": false, "run": RunSnapshot }` |
| 机器忙 / 待现场处理 | 409 | `ApiError { code: "machine_busy", message, currentRun }` |
| 两个硬件布尔同时为真 | 400 | `ApiError { code: "invalid_options" }` |
| 配置校验失败 | 400 | `ApiError { code: "config_invalid", issues: [...] }` |

- 幂等键是客户端生成的 `requestId`（字母/数字/`-`/`_`，≤64），不是 runId。
- 忙时不排队、不创建运行；`currentRun` 用于页面直接展示现有运行。
- 同源页面提交受 `Origin` 校验；无 Origin 的本机命令行请求放行。

## 3. SSE 事件

- 事件名与 `data.type` 使用同一套 camelCase：`runCreated / runPreparing / runPrepared / runStarted / runPhase / stepStarted / stepWaiting / stepFinished / stepUnknown / checkpointCreated / checkpointResolved / log / artifactCreated / cleanupStarted / cleanupFinished / pendingFieldEntered / pendingFieldResolved / lockReleased / runFinished`。
- 重连：`GET /api/runs/{runId}/events?after=<lastSequence>`；也接受 `Last-Event-ID`。
- 每条事件有 `protocolVersion / runId / sequence / at / type`；`sequence` 严格递增，客户端按它去重。
- 心跳：注释行 `: ping`，每 15 秒；不是事件。
- 终止：`runFinished` 是本轮最后一个业务事件，随后还有 `lockReleased`（若占用已解除）。流不会立即关闭，以支持待现场处理的后续事件；前端收到 `runFinished` 后可关闭并在历史中查看。
- 首次进入页面：先 `GET /api/runs/current` 拿快照，再按 `snapshot.lastSequence` 之后订阅；刷新同理。
- 断言样例见 `docs/api/events-sample.jsonl`（每行一条，可直接回放）。

## 4. 确认接口状态码

`POST /api/runs/{runId}/checkpoints/{checkpointId}/confirm`：

| 场景 | 状态码 | code |
| --- | --- | --- |
| 首次受理 | 202 | —（响应 `applied=false`，等执行器回执） |
| 同一 `requestId` 重复提交 | 202 | —（`replayed=true`） |
| 非当前/已处理确认点 | 409 | `checkpoint_not_current` |
| 已超时 | 409 | `checkpoint_expired` |
| 同一确认点已提交另一条命令、尚未回执 | 409 | `checkpoint_already_submitted` |
| 本轮已结束 | 409 | `run_not_waiting` |
| 阻塞/失败未填原因 | 400 | `reason_required` |

`runId + checkpointId + requestId` 三者缺一不可；后端只把命令写给当前等待点，执行器还会再次校验归属并回执。
“完成”只表示现场已操作，后续软件断言仍执行；没有强制通过接口。

## 5. 证据列表与预览

- `GET /api/runs/{runId}/artifacts` 返回本轮已登记证据；文件被移走时 `available=false`，但**不改写**原运行结论。
- `GET /api/runs/{runId}/artifacts/{artifactId}` 只按登记 ID 取，不接受路径；未登记或缺失返回 404 `artifact_not_found` / `artifact_missing`。
- MIME：`image/png`、`image/jpeg`、`text/plain`、`application/json`、`application/xml`；HTML 一律按下载，避免同源执行脚本。
- 步骤与证据的关联在 `StepState.evidenceIds`；本轮级证据 `stepId=""`。

## 6. 配置校验结构

- `GET /api/config` → `ConfigView`（`passwordConfigured` 只有布尔，无密码字段）。
- `PUT /api/config` 是**部分合并**：只更新非 null 字段；返回保存后的视图。
- `POST /api/config/validate` body 可带 `skipVersionAssertion`，返回：
  `{ valid, checkedAt, issues: [{field, code, message}], checks: [{id, label, status, detail}] }`。
- 版本/日期必填由**后端**判定：未跳过时缺失 → `issues`，`valid=false`；跳过时只记 warning，不阻塞。
- 开始执行时后端再次校验，页面禁用按钮不能替代后端校验。

## 7. 历史与报告

- `GET /api/runs?limit&offset` → `{ items: RunSummary[], total, limit, offset }`；无分页游标，支持至少两轮查询。
- `RunSummary`：runId、创建/结束时间、阶段、结论、硬件策略、版本跳过、清理状态、待现场处理、模拟标记、计数。
- `GET /api/runs/{runId}` → 完整快照；`GET /api/runs/{runId}/report` → 报告（步骤、选项、原因、清理、证据索引）。
- 时间均为带时区 `date-time`（服务器本地时区，ISO 8601）。历史只读，没有确认接口入口；旧确认点不能再次提交。

## 8. 部署与同源托管

- 一个发布包内两个入口：`WaferAoi.UiTestTool.Web`（net8.0，回环监听，默认 5180）与 `WaferAoi.UiTestTool.Executor`（net472）。
- 前端 `frontend/` 独立项目，构建产物由后端托管：`--web-root <frontend/dist>`；未指定时后端会向上查找 `frontend/dist`（开发期）。
- `base` 使用 `/`；开发期可用 Vite 代理 `/api` → `http://127.0.0.1:5180`，生产期直接同源，不存在跨域配置。
- 发布目录建议：
  ```
  publish/
    WaferAoi.UiTestTool.Web.exe
    web/                        前端 dist
    executor/WaferAoi.UiTestTool.Executor.exe
  ```
- 每次构建只需把 `frontend/dist` 覆盖到 `web/`，不需要重新编译后端。

## 9. Mock / 事件回放

- 后端提供 `--simulate [--scenario strict|skip-version|skip-hardware|manual|fail|pending-field]`：
  启动真实 Web 宿主 + 模拟执行器，覆盖六种选项组合、失败停止、确认等待、待现场处理和刷新恢复。
- `docs/api/events-sample.jsonl` 是事件样例，可按 sequence 回放；`tools/replay-events.ps1`（后续）读取样例写入运行目录。
- 模拟运行在快照和报告中带 `simulated=true`，页面必须显示“模拟运行，不代表实机通过”。
- 前端可在后端未就绪时先用 `--simulate` 联调，不需要自建第二套 mock 契约。

## 10. 认证、脱敏与边界

- 只监听 `127.0.0.1` / `::1`；不开放局域网，不需要登录账号体系。
- 浏览器修改类请求校验同源 `Origin`；无账号/CSRF token，因为攻击面限定为本机回环。
- 密码只来自 `WAFER_AOI_UI_PASSWORD`，不落盘、不出现在配置、快照、事件、报告、证据 URL 和日志。
- 文件接口只服务本轮登记产物，不提供任意路径、任意进程控制或 shell 命令。
- 执行器只接受 `--manifest` 指向的本轮清单，命令通道只接受确认点命令。

## 剩余未实现（不得当作已完成）

- 真实 UIA 27 步迁移：当前 `LiveFlow` 只完成独立生命周期并**显式以阻塞结束**，不启动被测程序；T04—T05 待做。
- 27 步严格能力表见 `docs/backend/step-capability.md`，缺口标为“待实现/待现场核验”。
