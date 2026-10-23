import { useState } from 'react';
import type { FieldResolutionResponse, FieldVerification, RunSnapshot } from '../types';
import { Badge } from './Badge';

const PROCEDURE = [
  '确认设备已停止运动，按现场规程处理晶圆与机械手。',
  '关闭本轮启动的被测程序；不要关闭身份不明的进程。',
  '核对工位无遗留晶圆，确认设备处于安全状态。',
  '在下方勾选两项现场事实后提交，由执行器核验并释放占用。',
];

interface PendingFieldCardProps {
  run: RunSnapshot;
  onResolve: (
    processExitedConfirmed: boolean,
    deviceStateConfirmed: boolean,
    note: string,
  ) => Promise<FieldResolutionResponse | null>;
  onRefresh: () => Promise<void>;
}

/** 待现场处理：占用不释放、禁止新一轮、没有强制解锁入口。 */
export function PendingFieldCard({ run, onResolve, onRefresh }: PendingFieldCardProps) {
  const [processExited, setProcessExited] = useState(false);
  const [deviceState, setDeviceState] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [verifications, setVerifications] = useState<FieldVerification[] | null>(null);

  // 换轮次由父组件的 key={runId} 隔离，避免把上一轮的勾选带过来。
  if (!run.pendingField.active) return null;

  const blockedStep = run.steps.find((step) => step.status === 'blocked' || step.status === 'failed');
  const ready = processExited && deviceState;

  const submit = async () => {
    if (!ready || busy) return;
    setBusy(true);
    try {
      const response = await onResolve(processExited, deviceState, note.trim());
      if (response?.verifications?.length) setVerifications(response.verifications);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card card-alert" aria-labelledby="pending-title">
      <div className="card-head">
        <h2 id="pending-title">待现场处理</h2>
        <Badge tone="blocked">占用未释放</Badge>
      </div>
      <p className="pf-lead">
        硬件状态未确认：<strong>不自动结束被测程序、不释放机器占用、禁止新一轮</strong>。本页不提供强制解锁入口。
      </p>
      <dl className="kv kv-tight">
        <dt>runId</dt>
        <dd className="mono">{run.runId}</dd>
        <dt>阻塞步骤</dt>
        <dd>{blockedStep ? `${blockedStep.stepId} ${blockedStep.title}` : '—'}</dd>
        <dt>原始结论</dt>
        <dd>{blockedStep?.reason || run.pendingField.reason || '—'}</dd>
        <dt>被测进程</dt>
        <dd className="mono">
          {run.pendingField.processId ? `WaferAOI.exe（PID ${run.pendingField.processId}）` : '未记录'}
        </dd>
      </dl>

      <p className="cp-label">现场处理规程</p>
      <ol className="cp-steps">
        {PROCEDURE.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ol>

      <p className="cp-label">现场确认</p>
      <label className="verify-check">
        <input
          id="pf-process-exited"
          type="checkbox"
          checked={processExited}
          disabled={busy}
          onChange={(event) => setProcessExited(event.target.checked)}
        />
        <span>被测进程已退出（执行器将按 PID 与启动时间核对）</span>
      </label>
      <label className="verify-check">
        <input
          id="pf-device-state"
          type="checkbox"
          checked={deviceState}
          disabled={busy}
          onChange={(event) => setDeviceState(event.target.checked)}
        />
        <span>设备状态已按规程确认（不含“强制解锁”语义）</span>
      </label>

      {verifications ? (
        <ul className="verify">
          {verifications.map((item) => (
            <li key={item.id}>
              <span className={`chk ${item.passed ? 'chk-ok' : 'chk-fail'}`}>{item.passed ? '已核验' : '未通过'}</span>
              {item.label}
              {item.detail ? <span className="muted">（{item.detail}）</span> : null}
            </li>
          ))}
        </ul>
      ) : null}

      <div className="field">
        <label htmlFor="pf-note">
          现场处理记录 <span className="muted">（处理人 / 时间 / 现状，随报告保存）</span>
        </label>
        <textarea
          id="pf-note"
          rows={2}
          maxLength={500}
          value={note}
          disabled={busy}
          onChange={(event) => setNote(event.target.value)}
        />
      </div>

      <div className="cp-actions">
        <button
          id="pf-refresh"
          type="button"
          className="btn btn-secondary"
          onClick={() => void onRefresh()}
          disabled={busy}
        >
          刷新执行器状态
        </button>
        <button
          id="pf-submit"
          type="button"
          className="btn btn-primary"
          onClick={() => void submit()}
          disabled={!ready || busy}
          aria-busy={busy}
        >
          提交现场处理完成
        </button>
      </div>
      <p className="hint">
        两项未勾选前无法提交；提交不是强制解锁，执行器核验进程与设备状态通过后才会释放占用。
      </p>
    </section>
  );
}
