import { useEffect, useState } from 'react';
import { Badge } from '../components/Badge';
import { Banner } from '../components/Banner';
import { StepList } from '../components/StepList';
import { CLEANUP_META, CONCLUSION_META, POLICY_LABEL, fmtDateTime } from '../domain';
import type { Session } from '../state/useSession';
import type { RunSnapshot } from '../types';

interface HistoryDetailViewProps {
  session: Session;
  runId: string;
  onBack: () => void;
}

/** 历史详情：只读，不出现任何确认或执行按钮。 */
export function HistoryDetailView({ session, runId, onBack }: HistoryDetailViewProps) {
  const [run, setRun] = useState<RunSnapshot | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    void (async () => {
      const loaded = await session.loadRun(runId);
      if (!alive) return;
      setRun(loaded);
      setLoading(false);
    })();
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runId]);

  // 历史详情同样使用记录里的计数，避免与后端投影出现两套口径。
  const counts = run ? run.counts : null;
  const conclusion = run ? CONCLUSION_META[run.conclusion] : null;

  return (
    <main className="view">
      <Banner tone="readonly">历史记录 · 只读 · {runId}</Banner>

      {loading ? (
        <section className="card">
          <p className="empty">正在读取历史记录…</p>
        </section>
      ) : null}

      {!loading && !run ? (
        <section className="card">
          <p className="empty">未找到该运行记录。</p>
        </section>
      ) : null}

      {run ? (
        <>
          <section className="card">
            <div className="card-head">
              <h2>{run.runId}</h2>
              <button type="button" className="btn btn-ghost btn-sm" onClick={onBack}>
                返回历史列表
              </button>
            </div>
            <dl className="kv kv-tight">
              <dt>开始时间</dt>
              <dd className="mono">{fmtDateTime(run.createdAt)}</dd>
              <dt>结束时间</dt>
              <dd className="mono">{fmtDateTime(run.finishedAt)}</dd>
              <dt>执行策略</dt>
              <dd>
                {POLICY_LABEL[run.options.hardwarePolicy]}
                {run.options.skipVersionAssertion ? ' + 跳过版本断言' : ''}
              </dd>
              <dt>预期版本 / 编译日期</dt>
              <dd className="mono">
                {run.config.expectedVersion || '未配置'} / {run.config.buildDate || '未配置'}
              </dd>
              <dt>证据目录</dt>
              <dd className="mono">{run.config.evidenceDir}</dd>
            </dl>

            <div className="counts" style={{ marginTop: 12 }}>
              <div className="count c-pass">
                <b>{counts?.passed}</b>
                <span>成功</span>
              </div>
              <div className="count c-skip">
                <b>{counts?.skipped}</b>
                <span>跳过</span>
              </div>
              <div className="count c-blocked">
                <b>{counts?.blocked}</b>
                <span>阻塞</span>
              </div>
              <div className="count c-failed">
                <b>{counts?.failed}</b>
                <span>失败</span>
              </div>
              <div className="count c-notrun">
                <b>{counts?.notRun}</b>
                <span>未执行</span>
              </div>
            </div>

            <p className="cleanup">
              结论：{conclusion ? <Badge tone={conclusion.tone}>{conclusion.label}</Badge> : null} · 清理：
              {CLEANUP_META[run.cleanup.status]}
              {run.cleanup.reason ? ` · ${run.cleanup.reason}` : ''}
            </p>
            <p className="hint">
              证据缺失时显示缺失态，不据此改写原运行结论；历史详情不含任何确认或执行入口。
            </p>
          </section>

          <section className="card">
            <div className="card-head">
              <h2>步骤结果</h2>
              <span className="muted">只读</span>
            </div>
            <StepList steps={run.steps} selected={null} now={Date.now()} readonly />
          </section>
        </>
      ) : null}
    </main>
  );
}
