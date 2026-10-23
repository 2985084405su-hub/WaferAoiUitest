import { CONCLUSION_META, CLEANUP_META, POLICY_LABEL, fmtDateTime } from '../domain';
import type { RunSummary } from '../types';
import { Badge } from './Badge';

interface HistoryTableProps {
  runs: RunSummary[];
  onOpen: (runId: string) => void;
}

export function HistoryTable({ runs, onOpen }: HistoryTableProps) {
  if (runs.length === 0) {
    return <p className="empty">暂无历史运行。完成至少一轮后在此查询。</p>;
  }

  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="sr-only">本机历史运行列表</caption>
        <thead>
          <tr>
            <th scope="col">runId</th>
            <th scope="col">开始时间</th>
            <th scope="col">执行策略</th>
            <th scope="col">结论</th>
            <th scope="col">清理</th>
            <th scope="col">成功 · 跳过 · 阻塞 · 失败 · 未执行</th>
          </tr>
        </thead>
        <tbody>
          {runs.map((run) => {
            const meta = CONCLUSION_META[run.conclusion];
            return (
              <tr
                key={run.runId}
                data-run={run.runId}
                tabIndex={0}
                role="link"
                aria-label={`打开运行 ${run.runId}`}
                onClick={() => onOpen(run.runId)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    onOpen(run.runId);
                  }
                }}
              >
                <td className="mono">{run.runId}</td>
                <td className="mono">{fmtDateTime(run.createdAt)}</td>
                <td>
                  {POLICY_LABEL[run.hardwarePolicy]}
                  {run.skipVersionAssertion ? ' + 跳过版本' : ''}
                </td>
                <td>
                  <Badge tone={meta.tone}>{meta.label}</Badge>
                </td>
                <td>{CLEANUP_META[run.cleanupStatus]}</td>
                <td className="mono">
                  {run.counts.passed} · {run.counts.skipped} · {run.counts.blocked} · {run.counts.failed} ·{' '}
                  {run.counts.notRun}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
