import { useState } from 'react';
import { fmtDateTime, fmtDuration } from '../domain';
import type { CheckpointResult, RunSnapshot } from '../types';
import { Badge } from './Badge';

interface CheckpointCardProps {
  run: RunSnapshot;
  now: number;
  onSubmit: (result: CheckpointResult, reason: string, note: string) => Promise<unknown>;
}

/** 人工确认卡：内联在清单上方，不用模态，日志仍可查看。 */
export function CheckpointCard({ run, now, onSubmit }: CheckpointCardProps) {
  const checkpoint = run.checkpoint;
  const step = run.steps.find((item) => item.stepId === checkpoint?.stepId);
  const [reason, setReason] = useState('');
  const [note, setNote] = useState('');
  const [reasonError, setReasonError] = useState('');
  const [busy, setBusy] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  // 上一轮的“已提交”状态由父组件用 key={checkpointId} 隔离，不在这里做状态重置。
  if (!checkpoint || checkpoint.status !== 'waiting' || !step) return null;
  // 执行被中断后快照里的 checkpoint 仍可能停在 waiting，但此时已没有执行器在等命令；
  // 只有本轮确实停在人工确认点时才提供操作，避免在待现场处理/已结束时显示旧确认按钮。
  if (run.phase !== 'waitingHuman' || run.pendingField.active || run.finishedAt) return null;

  const elapsed = now - Date.parse(checkpoint.createdAt);
  const instructions = checkpoint.instruction
    .split(/[；;]/)
    .map((item) => item.trim())
    .filter(Boolean);

  const submit = async (result: CheckpointResult) => {
    if (submitted || busy) return;
    if ((result === 'blocked' || result === 'failed') && !reason.trim()) {
      setReasonError('阻塞或失败必须填写原因。');
      return;
    }
    setReasonError('');
    setBusy(true);
    try {
      await onSubmit(result, reason.trim(), note.trim());
      setSubmitted(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card card-wait" aria-labelledby="checkpoint-title">
      <div className="card-head">
        <h2 id="checkpoint-title">人工确认点</h2>
        <Badge tone="waiting">等待人工确认</Badge>
      </div>
      <p className="cp-name">
        {step.stepId} {step.title}
      </p>
      <dl className="kv kv-tight">
        <dt>确认点 ID</dt>
        <dd className="mono">{checkpoint.checkpointId}</dd>
        <dt>等待起点</dt>
        <dd className="mono">{fmtDateTime(checkpoint.createdAt)}</dd>
        <dt>已等待</dt>
        <dd className="mono" aria-live="off">
          {fmtDuration(elapsed)}
        </dd>
        <dt>过期时间</dt>
        <dd className="mono">{fmtDateTime(checkpoint.expiresAt)}</dd>
      </dl>

      <p className="cp-label">现场操作说明</p>
      <ol className="cp-steps">
        {instructions.map((item, index) => (
          <li key={index}>{item}</li>
        ))}
      </ol>

      <div className="field">
        <label htmlFor="cp-note">
          备注 <span className="muted">（现场情况，可留空）</span>
        </label>
        <textarea
          id="cp-note"
          rows={2}
          maxLength={500}
          value={note}
          disabled={busy || submitted}
          onChange={(event) => setNote(event.target.value)}
        />
      </div>
      <div className={`field${reasonError ? ' is-invalid' : ''}`}>
        <label htmlFor="cp-reason">
          原因 <span className="muted">（报告阻塞或判定失败时必填）</span>
        </label>
        <textarea
          id="cp-reason"
          rows={2}
          maxLength={500}
          value={reason}
          disabled={busy || submitted}
          aria-invalid={reasonError ? true : undefined}
          onChange={(event) => {
            setReason(event.target.value);
            setReasonError('');
          }}
        />
        {reasonError ? <p className="err">{reasonError}</p> : null}
      </div>

      <div className="cp-actions">
        <button
          id="cp-done"
          type="button"
          className="btn btn-primary"
          onClick={() => void submit('done')}
          disabled={busy || submitted}
          aria-busy={busy}
        >
          完成（现场已操作）
        </button>
        <button
          id="cp-blocked"
          type="button"
          className="btn btn-warning"
          onClick={() => void submit('blocked')}
          disabled={busy || submitted}
        >
          报告阻塞
        </button>
        <button
          id="cp-failed"
          type="button"
          className="btn btn-danger"
          onClick={() => void submit('failed')}
          disabled={busy || submitted}
        >
          判定失败
        </button>
      </div>
      <p className="hint">
        “完成”仅表示现场已操作，随后仍执行软件断言。重复提交同一确认点按幂等处理；过期或已结束的确认点提交会被拒绝且不改变结果。
      </p>
    </section>
  );
}
