import { POLICY_LABEL, convergedCount, fmtDuration, rangeNoteText } from '../domain';
import type { RangeInfo, RunSnapshot, StepCounts } from '../types';
import { Badge } from './Badge';

function conclusionText(run: RunSnapshot, counts: StepCounts, sectionText: string): string {
  const mode = POLICY_LABEL[run.options.hardwarePolicy];
  // 章节前缀由本轮实际步骤推导，避免把 01—07 的运行写成 01—06。
  const scope = sectionText || '本轮';
  if (run.pendingField.active || run.conclusion === 'pendingField') {
    return '待现场处理：硬件状态未确认，占用未释放。现场处理完成后不把原结论改为成功，也不自动续跑。';
  }
  const failed = run.steps.find((step) => step.status === 'failed');
  if (run.conclusion === 'failed' && failed) {
    return `${scope}执行未通过：${failed.stepId} ${failed.title} 失败后立即停止，其余 ${counts.notRun} 步保持未执行。`;
  }
  const blocked = run.steps.find((step) => step.status === 'blocked');
  if (run.conclusion === 'blocked' && blocked) {
    return `${scope}执行阻塞：${blocked.stepId} ${blocked.title} 无法满足条件，已停止，其余 ${counts.notRun} 步保持未执行。`;
  }
  if (run.conclusion === 'interrupted') {
    return `运行中断，最后事件时间 ${run.executor.lastEventAt ? new Date(run.executor.lastEventAt).toLocaleString() : '未知'}；当前步骤结果未知，未开始步骤仍未执行。`;
  }
  if (run.cleanup.status === 'failed') {
    return '步骤已结束，但清理失败：本轮不能显示为全部成功。';
  }
  if (run.conclusion === 'passed' || run.conclusion === 'passedWithSkips') {
    const skipNote = counts.skipped > 0 ? `，其中 ${counts.skipped} 步按选项跳过` : '';
    return `${scope}执行完成（${mode}）：${counts.total} 步已收敛${skipNote}。本结论不代表整条实机主流程通过。`;
  }
  return `执行中（${mode}）：已收敛 ${convergedCount(counts)} / ${counts.total} 步。`;
}

interface RunSummaryProps {
  run: RunSnapshot | null;
  now: number;
  /** 本轮记录的范围声明；没有运行时退回后端 catalog 声明。 */
  range: RangeInfo | null;
  /** 当前展示步骤的章节范围文案，如「01—07 节」。 */
  sectionRange: string;
}

export function RunSummary({ run, now, range, sectionRange }: RunSummaryProps) {
  const counts = run?.counts;
  const liveStep = run?.steps.find((step) => step.status === 'running' || step.status === 'waiting');
  const converged = counts ? convergedCount(counts) : 0;
  // 分母只认本轮快照；没有快照时用范围声明，不再退回写死的 27。
  const total = counts?.total ?? range?.implemented ?? 0;
  const runningFor = liveStep?.status === 'running' && liveStep.startedAt
    ? now - Date.parse(liveStep.startedAt)
    : null;

  let live = '尚未开始执行。';
  if (run) {
    if (liveStep?.status === 'waiting') live = `等待人工确认：${liveStep.stepId} ${liveStep.title}`;
    else if (liveStep) live = `正在执行：${liveStep.stepId} ${liveStep.title}${runningFor != null ? `（${fmtDuration(runningFor)}）` : ''}`;
    else if (run.phase === 'pendingField') live = '待现场处理：硬件状态未确认，占用未释放。';
    else if (run.phase === 'cleaning') live = '清理阶段：步骤结果与清理结果分开记录。';
    else if (run.phase === 'finished') live = '本轮已结束。';
    else live = '准备中。';
  }

  return (
    <section className="card" aria-labelledby="summary-title">
      <div className="card-head">
        <h2 id="summary-title">本轮汇总</h2>
        <span className="muted">
          已收敛步骤（分母为本期{sectionRange ? ` ${sectionRange}` : ''} {range?.implemented ?? total} 步，不是完整设计{' '}
          {range?.designed ?? '—'} 步）
        </span>
      </div>

      <div className="counts" id="counts">
        <div className="count c-pass">
          <b>{counts?.passed ?? 0}</b>
          <span>成功{counts?.manualPassed ? `（含人工 ${counts.manualPassed}）` : ''}</span>
        </div>
        <div className="count c-skip">
          <b>{counts?.skipped ?? 0}</b>
          <span>跳过</span>
        </div>
        <div className="count c-blocked">
          <b>{counts?.blocked ?? 0}</b>
          <span>阻塞</span>
        </div>
        <div className="count c-failed">
          <b>{counts?.failed ?? 0}</b>
          <span>失败</span>
        </div>
        <div className="count c-notrun">
          <b>{counts?.notRun ?? total}</b>
          <span>未执行</span>
        </div>
      </div>

      <div className="extra-counts">
        {counts?.waiting ? (
          <>
            <Badge tone="waiting">等待人工确认</Badge>
            <span className="muted">等待中 {counts.waiting} · 不并入未执行</span>
          </>
        ) : null}
        {counts?.unknown ? (
          <>
            <Badge tone="unknown">中断·结果未知</Badge>
            <span className="muted">中断 {counts.unknown}</span>
          </>
        ) : null}
        <span className="muted">
          {counts
            ? `合计 ${counts.total} / ${range?.implemented ?? counts.total}`
            : `本期范围 ${range?.implemented ?? 0} 步`}
        </span>
      </div>

      <div className="progress" aria-hidden="true">
        <span style={{ width: `${total > 0 ? Math.min((converged / total) * 100, 100) : 0}%` }} />
      </div>

      <p className="range-note">{rangeNoteText(range, sectionRange)}</p>
      <p className="live" role="status" aria-live="polite">
        {live}
      </p>
      {run ? (
        <div className="result-row">
          <span className="muted">结论：{conclusionText(run, counts ?? run.counts, sectionRange)}</span>
        </div>
      ) : null}
    </section>
  );
}
