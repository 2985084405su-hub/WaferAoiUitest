# 前端对真后端的契约核验（JCRJZDH-6）

日期：2026-10-23。核验对象：`backend/` 的 Contracts / Web / Executor 实际运行结果，不是自建 mock。
前端：`frontend/`（React + TypeScript + Vite），`npm run lint`、`npm run build` 均通过。

## 1. 怎么跑起来的（本机可复现）

`D:\WaferAoiUitest` 所在目录有透明加密，直接 `dotnet build` 会 `CS2015：… 是二进制文件而非文本文件`
（csc 读到的是密文）。**拷贝目录也会保留加密**，`robocopy` 到临时目录同样失败。

可用做法：用 git 把当前提交导出到未加密目录再构建：

```powershell
git archive --format=tar HEAD -o D:\tmp\h6.tar
# 解包到 D:\tmp\h6 后
dotnet build D:\tmp\h6\backend\WaferAoi.UiTestTool.sln -c Debug
```

本次核验即在此方式下完成：Contracts / Web / Executor **0 警告 0 错误**。
前端 `npm run build` 产出的 `frontend/dist` 由 Web 宿主 `--web-root` 托管，静态资源与 `/api` 同源可访问。

## 2. 六种运行选项组合 + 异常退出（模拟执行器）

每个场景独立数据目录、独立端口，轮询快照并在确认点提交网页确认，结束后核对快照、`events.jsonl`、历史与 SSE 补流。

| 场景 | 结果 | 计数（passed/manual/skipped/failed/notRun/waiting/unknown/总） |
| --- | --- | --- |
| `strict` | finished / `passed`，确认点 03.01、03.02、06.01 共 3 个 | 27/3/0/0/0/0/0/27 |
| `skip-version` | finished / `passedWithSkips`，01.02 记 `userSkipped` 且原因写明选项 | 26/3/1/0/0/0/0/27 |
| `skip-hardware` | finished / `passedWithSkips`，无确认点，整步与混合硬件分别记跳过 | 19/0/8/0/0/0/0/27 |
| `manual` | finished / `passed`，6 个网页确认点全部 `operatorConfirmed` | 27/6/0/0/0/0/0/27 |
| `fail` | 04.03 失败即停，其后 18 步保持 `notRun`，清理单独记 `succeeded` | 8/2/0/1/18/0/0/27 |
| `pending-field` | `pendingField` + 占用保持；新运行 409 `machine_busy`；现场处理核验后解除且不改写原结论 | 27/3/0/0/0/0/0/27 |
| 执行器被杀（异常退出） | 当前步 `unknown`、进入待现场处理、22 步未执行；宿主重启后仍保持占用并拒绝新运行 | 快照见 `unknown=1` |

额外核验点：

- 创建幂等：同一 `requestId` 第二次提交返回 200 / `created=false`，`runId` 相同；机器忙返回 409 并带 `currentRun`。
- 确认点：错误 `checkpointId` → 409 `checkpoint_not_current`；阻塞/失败不填原因 → 400 `reason_required`；
  同一 `requestId` 重放 → 202 `replayed=true`；同一确认点换 `requestId` → 409。
- 快照字段、步骤字段、`counts` 键名（`waiting`、`unknown` 单列）、`phase`/`conclusion`/`cleanupStatus` 取值
  与 `frontend/src/types.ts` 完全一致；SSE 的 `event:` 名与 `data.type` 一致，`sequence` 1..N 无缺口。
- `hardwarePolicy` 三值：`strict | skip | manual`，两个都不选即 `strict`；快照与报告保存同一份选项。

## 3. 发现的后端缺陷（前端侧不掩盖，交由后端修）

> 2026-10-24 更新：3.1、3.2 均已在后端 `3b79372` 修复并随 `a52968c` 并入共用分支，
> 下面保留的是当时的复现记录和影响判断。

### 3.1 宿主合成事件被写成多行 JSON，破坏 `events.jsonl` 一行一条

- 现象：每个场景的 `events.jsonl` 行数都大于事件数（如 81 行 / 73 条）。`lockReleased` 等事件是缩进格式的多行 JSON。
- 位置：`backend/src/WaferAoi.UiTestTool.Web/Runs/RunStore.cs:268`（`AppendSyntheticEvent` 用 `JsonDefaults.File` 序列化），
  而 `JsonDefaults.File` 在 `backend/src/WaferAoi.UiTestTool.Web/JsonDefaults.cs:13,23` 是 `WriteIndented = true`。
  执行器自己写的事件是单行，所以只有宿主补写的事件（`lockReleased`、`pendingFieldResolved`、
  中断时的 `stepUnknown`/`pendingFieldEntered`/`runFinished`）会变形。
- 影响（已实测）：宿主重启后 `RunStore.ReadEvents` 按行解析，跳过这条多行记录，SSE 从 0 补流少 1 条
  （73 条落盘，只重放 72 条，且缺的正是 `lockReleased`）。中断场景更明显：48 行只解析出 19 条。
  页面的“占用已解除”只能靠快照兜底，任何按事件回放的工具会丢终结记录。
- 建议：为事件行单独定义 `WriteIndented = false` 的序列化选项（例如 `JsonDefaults.Line`），仅 `AppendSyntheticEvent` 使用；
  报告/清单继续缩进。

### 3.2 执行器已退出，快照仍显示 `executor.alive = true`

- 现象：杀掉执行器后，快照 `phase=pendingField`、`conclusion=pendingField`、当前步 `unknown`，
  但 `executor.alive` 仍为 `true`、`exitCode=-1`。
- 位置：`EventProjector.cs:35` 对每条事件无条件执行 `snapshot.Executor.Alive = true`，
  而中断时宿主补写的 `stepUnknown` / `pendingFieldEntered` / `runFinished` 会把 `FinalizeAsync` 刚设的 `false` 覆盖回去。
- 影响：任何直接读 `executor.alive` 的展示会说“执行器运行中”，与 WEB-08“执行器异常退出时结束正常运行显示”冲突。
  当前前端只用 `executor.lastEventAt` 展示中断时间、用 `phase`/`conclusion` 表达状态，所以页面没有被误导，
  但这个字段本身不可信。
- 建议：`Alive` 只由执行器生命周期（启动/退出）决定，投影事件时不要改写；或仅对执行器来源的事件置真。

## 4. 前端跟着改的一处

`useSession` 与历史详情原先用本地 `computeCounts(steps)` 重算计数，与契约“页面只消费后端字段、不做二次推导”不一致，
会形成两套口径。现改为直接使用快照/记录里的 `counts`（`frontend/src/state/useSession.ts`、
`frontend/src/views/HistoryDetailView.tsx`），`computeCounts` 仅保留给模拟后端生成数据。
本次六个场景的快照计数与本地重算结果一致，所以界面数值不变，只是口径收口。

## 5. 没有验证到的

- 真实 27 步 UIA 执行：`LiveFlow` 仍显式阻塞，未启动被测程序、未操作设备；本文件所有结论都只来自 `--simulate`。
- `checkpoint_expired`：确认点超时固定 30 分钟，本次没有等待触发，只核到 `checkpoint_not_current`、
  `checkpoint_already_submitted`（并发提交时出现）与 `run_not_waiting` 的代码分支。
- 局域网/多宿主并发、浏览器长期断线重连的真实网络条件。
- 页面在 320px 窄屏与读屏下的表现：本次只做了 1500×2400 的渲染截图，未做设备矩阵。

## 6. 合并后复验（2026-10-24，共用分支 `a52968c`，前端修复 `a1ef72e`）

合并树 = 前端 `550cbc7` + 后端 `3b79372`。后端在合并树（`git archive` 到未加密目录）重新构建：
Contracts / Web / Executor **0 警告 0 错误**；前端 `npm run lint`、`npm run build` 通过，`dist` 重新生成
（271.93 kB / gzip 83.83 kB，mock 仍单独成 chunk）。

后端场景用合并后的 Web 宿主 + 执行器 `--simulate`（每个场景独立数据目录与端口）：

| 场景 | 快照 | 事件落盘 | 重启补流 |
| --- | --- | --- | --- |
| strict | `finished` / `passed`，清理 `succeeded`，成功 27（含人工 3）/ 总计 27，`alive=false`、`exitCode=0` | **73 行 = 73 条**，末条 `lockReleased`，无坏行 | `after=0` **73/73**（活动会话与宿主重启后一致） |
| 执行器被杀（停 03.01） | `pendingField` / `pendingField`，当前步 03.01 `unknown`，成功 4 / 未执行 22 / 未知 1，`alive=false`、`exitCode=-1`，新运行 409 `machine_busy` | **19 行 = 19 条**，末条 `runFinished`，无 `lockReleased` | `after=0` **19/19**；重启后 `alive` 仍为 `false`，占用保持、仍 409 |
| pending-field | `pendingField` / `pendingField`，成功 27（含人工 3），`alive=false`，未勾选两项提交 400，运行中 409 | 解除前 70/70；解除后 **72 行 = 72 条**（新增单行 `pendingFieldResolved` + `lockReleased`） | `after=0` **72/72** |

- `events.jsonl` 每行都是单行 JSON（strict 最长 1069 字符，`badLines=0`）；`manifest.json`、`report.json` 仍是缩进格式。
- 现场处理解除后 `phase=finished`，`conclusion` 仍为 `pendingField`（原结论未被改写），报告与历史同口径。
- 中断后 `executor.alive=false`，宿主重启、补写 `stepUnknown` / `pendingFieldEntered` / `runFinished` 后都没有回弹。

浏览器侧（headless Chrome + CDP，真实 `dist` + 真后端）：

- strict：网页开始执行，03.01 / 03.02 / 06.01 三个确认点逐个提交；结束后计数 `27 / 0 / 0 / 0 / 0`、
  结论“通过”、清理“清理成功”、03.01 显示“成功·人工确认”；1680 与 1280 宽度横向溢出均为 0。
- 执行器被杀：显示“占用未释放 / 待现场处理”、`中断·结果未知 1`、03.01 为“中断·结果未知”，
  “开始执行”禁用并提示“待现场处理：占用未释放，禁止新一轮。”；页面不再出现“执行器运行中”。
- 历史：列表与详情都只读，详情无任何确认/执行按钮；被杀一轮在历史中为“待现场处理 / 4·0·0·0·22”。
- 控制台：0 个 JS 错误、0 个未处理 Promise；只有一条 `favicon.ico` 404，已在本轮加 `favicon.svg` 链接修掉。

**本轮修的前端问题（`a1ef72e`）**：中断后后端快照里的 `checkpoint.status` 仍是 `waiting`（`PendingFieldEntered`
不清空 checkpoint），页面会继续渲染可点击的“完成 / 报告阻塞 / 判定失败”，与“待现场处理”自相矛盾；
点击后虽被后端 409 `run_not_waiting` 拒绝，但属于误导性操作入口。现改为仅当
`phase=waitingHuman` 且未进入待现场处理、未结束时才显示确认卡。

**关于第 4 点（前端兜底逻辑）**：前端不读 `events.jsonl`、不按行数校验事件，也不展示或回弹
`executor.alive`（`alive` 只在 `types.ts` 与 `mock.ts` 出现，页面只用 `lastEventAt`/`phase`/`conclusion`）。
因此没有“多行事件/行数大于事件数”或 `alive` 回弹的兜底需要移除。保留的两处是传输层韧性，与本次契约修复无关：
SSE 单帧 `JSON.parse` 失败时跳过该帧；以及仅在 `connection=lost` 时每 1.5s 轮询快照（运行结束即停止）。

**仍待后端确认的契约点**：中断或进入待现场处理后，快照里的 `checkpoint` 仍为 `waiting`。
建议在 `PendingFieldEntered`/`MarkInterruptedAsync` 时清空或标记为 `expired`，让任何按快照渲染的消费者
都不再把它当成可提交的确认点；前端目前的 `phase` 门控只是防御。

