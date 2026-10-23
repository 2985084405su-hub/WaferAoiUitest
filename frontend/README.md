# 主流程自动化回归 · 本机 Web 执行面板（前端）

对应任务：JCRJZDH-6 / WAOI-MAINFLOW-WEB。范围只有 01—06 节 27 步。

## 技术栈与运行

- React 19 + TypeScript 6 + Vite 8，无 UI 框架依赖，样式用设计包里的 `tokens.css`（原样引入）+ 一个 `app.css`。
- 构建产物进 `dist/`；后端 `LocalPaths.FindFrontendDist()` 会从宿主目录向上找到 `frontend/dist` 或 `frontend`，发布包也可用 `--web-root` 显式指定。

```bash
npm install
npm run dev      # http://127.0.0.1:5173，/api 代理到 127.0.0.1:5180
npm run build    # tsc -b && vite build -> dist/
npm run lint     # oxlint
npm run preview  # 预览构建产物
```

**后端没起时**：页面不会白屏，会切到明确标注的“模拟数据（simulated）”后端，并在顶部显示黄色说明条；该条与 mock 代码只在 `createBackend()` 探测失败时出现，正式联调不会渲染。

## 目录

```
src/
├─ main.tsx / App.tsx        入口与视图切换（当前运行 / 历史列表 / 历史详情）
├─ types.ts                  契约类型（与 Contracts\*.cs 的 JSON 输出一致）
├─ domain.ts                 枚举 → 文案/颜色的唯一映射 + 计数 + 格式化
├─ api/backend.ts            真实 HTTP + SSE 客户端（路由与 ApiEndpoints.cs 对齐）
├─ api/mock.ts               模拟后端（simulated=true，仅探测失败时懒加载）
├─ state/useSession.ts       服务端状态 + 订阅/补流/刷新，客户端状态就近
├─ components/               Badge、StepList、RunSummary、CheckpointCard、PendingFieldCard…
├─ views/                    CurrentRunView / HistoryView / HistoryDetailView
└─ styles/                   tokens.css（设计包原样）、app.css
```

状态边界：快照、历史、配置只来自后端；组件不根据日志或耗时推导步骤结论。事件只用于触发刷新、补齐与日志，步骤状态一律以快照为准。

## 与后端的契约（已对齐实际实现）

| 用途 | 路由 |
| --- | --- |
| 步骤定义 | `GET /api/catalog/steps` → `{ implemented, designed, unimplemented, steps[] }` |
| 配置 | `GET/PUT /api/config`、`POST /api/config/validate`（body `{ skipVersionAssertion }`） |
| 当前运行 | `GET /api/runs/current` |
| 创建运行 | `POST /api/runs`（`requestId` 幂等；`options{skipVersionAssertion, hardwarePolicy}`） |
| 历史 | `GET /api/runs?limit&offset` → `{ items, total, limit, offset }` |
| 单轮 | `GET /api/runs/{runId}`、`GET /api/runs/{runId}/report` |
| 事件 | `GET /api/runs/{runId}/events?after=seq`（**命名 SSE 事件**，前端逐个 `addEventListener`） |
| 确认 | `POST /api/runs/{runId}/checkpoints/{id}/confirm`（202，`accepted/replayed/applied`） |
| 现场处理 | `POST /api/runs/{runId}/field-resolution` |
| 证据 | `GET /api/runs/{runId}/artifacts/{artifactId}` |

`hardwarePolicy` 只有 `strict | skip | manual`；两个硬件复选框都未选 = `strict`，前端不保存两个互斥布尔。

## 已实测（不是目测）

**A. 真后端联调**（Web 宿主 + 执行器 `--simulate` + 真实 SSE，127.0.0.1）：

| 模式 | 结果 |
| --- | --- |
| 严格执行 | 27 步收敛，成功 27（含人工 3），确认点 03.01 / 03.02 / 06.01 逐个提交，清理成功 |
| 人工执行硬件 | 确认点 6 个（02.02、03.01、03.02、06.01、06.03、06.04），成功 27（含人工 6） |
| 跳过硬件 | 0 个确认点，成功 19 / 跳过 8 |
| 实时性 | 不点击任何按钮，首屏状态在 948–967ms 内自动推进（SSE 生效） |
| 历史 | 真 `/api/runs` 返回记录，详情只读、无主操作/危险按钮 |
| 控制台 | 0 错误；1680 / 1280 / 1024 三档横向溢出均为 0px |

**B. 模拟后端**（覆盖真后端当前拿不到的分支）：失败停止（04.03 失败后其后 18 步保持未执行）、待现场处理（占用不释放、两项未勾选不能提交、核验后解除）、断线横幅。

## 已知缺口

1. **失败/待现场处理分支未在真后端上跑过**：真执行器需要以 `--scenario fail|pending` 启动宿主才能复现；前端逻辑已在 mock 上验证，联调时再补一次。
2. **证据预览**未做：卡片显示登记信息与 `available` 状态，点击下载/预览尚未接线；报告弹层目前直接渲染 `/report` 的 JSON，未做版式。
3. **确认点的“重新核验现场状态”**：契约里只有 `field-resolution` 一次提交（提交后返回三项核验），没有独立核验接口，所以页面用“两项现场确认 + 提交 → 展示后端核验结果”，与设计稿的两次交互略有差异。
4. **人工策略的确认点数量**：设计要求 8 个（所有涉及硬件的步骤），后端 catalog 目前标了 6 个（01.01、04.01 仍为自动）。需要后端补 `manualRequirement` 或确认口径。
5. **无自动化测试工程**：本轮用 CDP 脚本做端到端走查（脚本不在仓库内），尚未接入 CI。

## 无障碍与性能

- 步骤行是原生 `button`，`aria-current="step"` 标当前步骤；确认/现场卡用真实 `label` 关联控件；Toast 与实时状态行用 `role="status"` / `aria-live="polite"`。
- 状态同时用中文与颜色表达，颜色不是唯一手段；`prefers-reduced-motion` 下关闭旋转与补间。
- 首屏包 271 kB（gzip 83.8 kB），mock 后端单独成 chunk（20.9 kB）并按需加载；未做路由级拆包（单页三视图，收益不大）。
- 未跑读屏实测与真实字体回退验证，这部分需要在现场机器上补。
