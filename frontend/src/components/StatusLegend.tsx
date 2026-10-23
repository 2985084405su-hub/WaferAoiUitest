import type { StepStatus } from '../types';
import { StatusBadge } from './Badge';

/** 11 个状态必须同时有色与中文；这里作为常驻图例，不能只用颜色区分。 */
const ORDER: StepStatus[] = [
  'notRun',
  'running',
  'waiting',
  'passed',
  'operatorConfirmed',
  'passedWithHardwareSkipped',
  'hardwareSkipped',
  'userSkipped',
  'blocked',
  'failed',
  'unknown',
];

export function StatusLegend() {
  return (
    <section className="card" aria-labelledby="legend-title">
      <div className="card-head">
        <h2 id="legend-title">状态图例</h2>
        <span className="muted">色 + 文字双编码</span>
      </div>
      <ul className="legend">
        {ORDER.map((status) => (
          <li key={status}>
            <StatusBadge status={status} />
          </li>
        ))}
      </ul>
    </section>
  );
}
