import { useEffect, useState } from 'react';
import { CheckpointCard } from '../components/CheckpointCard';
import { DetailPanel } from '../components/DetailPanel';
import { ConfigPanel } from '../components/ConfigPanel';
import { OptionsPanel } from '../components/OptionsPanel';
import { PendingFieldCard } from '../components/PendingFieldCard';
import { PhaseStepper } from '../components/PhaseStepper';
import { RunSummary } from '../components/RunSummary';
import { StepList } from '../components/StepList';
import { StatusLegend } from '../components/StatusLegend';
import { Badge } from '../components/Badge';
import {
  CLEANUP_META,
  CONCLUSION_META,
  fmtDateTime,
  optionSnapshotText,
} from '../domain';
import type { Session } from '../state/useSession';
import type { PreparationCheck, RunSnapshot } from '../types';

function PrepChecks({ checks }: { checks: PreparationCheck[] | null }) {
  return (
    <section className="card" aria-labelledby="prep-title">
      <div className="card-head">
        <h2 id="prep-title">准备检查</h2>
        <span className="muted">{checks ? '已完成' : '尚未开始'}</span>
      </div>
      <ul className="checklist">
        {checks ? (
          checks.map((check) => {
            const cls =
              check.status === 'ok'
                ? 'chk-ok'
                : check.status === 'warning'
                  ? 'chk-warn'
                  : check.status === 'failed'
                    ? 'chk-fail'
                    : 'chk-idle';
            const label = { ok: '通过', warning: '注意', failed: '失败' }[check.status] || '待检查';
            return (
              <li key={check.id}>
                <span className={`chk ${cls}`}>{label}</span>
                <span>
                  {check.label}
                  {check.detail ? (
                    <>
                      <br />
                      <span className="muted">{check.detail}</span>
                    </>
                  ) : null}
                </span>
              </li>
            );
          })
        ) : (
          <li>
            <span className="chk chk-idle">·</span>点击“开始执行”后由后端执行启动前检查
          </li>
        )}
      </ul>
      <p className="hint">准备检查不计作用例步骤通过。</p>
    </section>
  );
}

function SnapshotCard({ run }: { run: RunSnapshot }) {
  return (
    <section className="card" aria-labelledby="snapshot-title">
      <div className="card-head">
        <h2 id="snapshot-title">本轮快照</h2>
        <span className="muted">只读</span>
      </div>
      <dl className="kv">
        <dt>runId</dt>
        <dd className="mono">{run.runId}</dd>
        <dt>开始时间</dt>
        <dd className="mono">{fmtDateTime(run.createdAt)}</dd>
        <dt>选项快照</dt>
        <dd>{optionSnapshotText(run.options)}</dd>
        <dt>运行标记</dt>
        <dd>
          {run.simulated ? (
            <span className="muted">模拟运行（simulated=true），不代表实机通过</span>
          ) : (
            '实机运行'
          )}
        </dd>
      </dl>
    </section>
  );
}

function ConclusionCard({ run }: { run: RunSnapshot | null }) {
  const meta = CONCLUSION_META[run?.conclusion ?? 'pending'];
  return (
    <section className="card" aria-labelledby="conclusion-title">
      <div className="card-head">
        <h2 id="conclusion-title">本轮结论</h2>
        <Badge tone={meta.tone}>{meta.label}</Badge>
      </div>
      <p className="cleanup">
        清理：{CLEANUP_META[run?.cleanup.status ?? 'notStarted']}
        {run?.cleanup.reason ? ` · ${run.cleanup.reason}` : ''}
        {run?.cleanup.reason?.endsWith('。') ? '' : '。'}
        步骤结论与清理结果分开记录，清理失败时不会显示全部成功。
      </p>
    </section>
  );
}

export function CurrentRunView({ session }: { session: Session }) {
  const { run, now, selectedStep, setSelectedStep, logs, prepChecks } = session;
  const [report, setReport] = useState<unknown | null>(null);
  const [reportOpen, setReportOpen] = useState(false);
  const step = run?.steps.find((item) => item.stepId === selectedStep) ?? null;
  const reportReady = Boolean(run && (run.phase === 'finished' || run.pendingField.active));

  useEffect(() => {
    if (!reportOpen) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setReportOpen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [reportOpen]);

  const openReport = async () => {
    const loaded = await session.loadReport();
    if (loaded) {
      setReport(loaded);
      setReportOpen(true);
    }
  };

  return (
    <div className="layout">
      <aside className="col col-left" aria-label="运行配置与选项">
        <ConfigPanel session={session} />
        <OptionsPanel session={session} />
        <PrepChecks checks={prepChecks} />
        {run ? <SnapshotCard run={run} /> : null}
      </aside>

      <section className="col col-main">
        <PhaseStepper phase={run?.phase ?? null} />
        <RunSummary run={run} now={now} />

        {run ? (
          <CheckpointCard
            key={run.checkpoint?.checkpointId ?? 'no-checkpoint'}
            run={run}
            now={now}
            onSubmit={(result, reason, note) => session.confirmCheckpoint(result, reason, note)}
          />
        ) : null}

        {run ? (
          <PendingFieldCard
            key={run.runId}
            run={run}
            onResolve={(processExited, deviceState, note) =>
              session.resolvePendingField(processExited, deviceState, note)
            }
            onRefresh={() => session.loadRun(run.runId).then(() => undefined)}
          />
        ) : null}

        <section className="card" aria-labelledby="steps-title">
          <div className="card-head">
            <h2 id="steps-title">步骤清单</h2>
            <span className="muted">27 步 · 按章节</span>
          </div>
          <StepList
            steps={run?.steps ?? session.idleSteps}
            selected={selectedStep}
            onSelect={setSelectedStep}
            now={now}
          />
          {!run && session.idleSteps.length === 0 ? (
            <p className="empty">
              后端尚未提供步骤定义接口（GET /api/steps），27 步清单将在开始执行后由快照下发。
            </p>
          ) : null}
        </section>

        <ConclusionCard run={run} />
        <div className="row-actions">
          <button
            id="btn-report"
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => void openReport()}
            disabled={!reportReady}
          >
            查看报告
          </button>
        </div>
      </section>

      <aside className="col col-right" aria-label="步骤详情">
        <DetailPanel
          run={run}
          step={step}
          definition={session.catalog?.find((item) => item.stepId === selectedStep) ?? null}
          logs={logs}
        />
        <StatusLegend />
      </aside>

      {reportOpen ? (
        <div className="modal-backdrop" onClick={() => setReportOpen(false)}>
          <div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="report-title"
            onClick={(event) => event.stopPropagation()}
          >
            <div className="modal-head">
              <h2 id="report-title">本轮报告</h2>
              <button type="button" className="btn btn-ghost btn-sm" onClick={() => setReportOpen(false)}>
                关闭
              </button>
            </div>
            <pre className="pre">{JSON.stringify(report, null, 2)}</pre>
          </div>
        </div>
      ) : null}
    </div>
  );
}
