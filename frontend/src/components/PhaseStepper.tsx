import type { RunPhase } from '../types';

const SEGMENTS = [
  { key: 'preparing', label: '准备' },
  { key: 'executing', label: '执行' },
  { key: 'waitingHuman', label: '等待人工' },
  { key: 'cleaning', label: '清理' },
  { key: 'finished', label: '结束' },
] as const;

function activeIndex(phase: RunPhase | null): number {
  switch (phase) {
    case null:
      return -1;
    case 'created':
    case 'preparing':
      return 0;
    case 'executing':
      return 1;
    case 'waitingHuman':
      return 2;
    case 'cleaning':
      return 3;
    case 'finished':
      return 4;
    default:
      return -1;
  }
}

/** 阶段与步骤结果分开显示：清理进行中时步骤仍显示各自终态。 */
export function PhaseStepper({ phase }: { phase: RunPhase | null }) {
  const pending = phase === 'pendingField';
  const current = activeIndex(phase);

  return (
    <section className="card card-flush" aria-label="运行阶段">
      <ol className="stepper">
        {SEGMENTS.map((segment, index) => {
          const isPendingSlot = pending && index === 3;
          let state = '';
          if (isPendingSlot) {
            state = 'is-pending';
          } else if (pending) {
            state = index < 3 ? 'is-done' : '';
          } else if (current >= 0 && index < current) {
            state = 'is-done';
          } else if (current >= 0 && index === current) {
            state = 'is-current';
          }
          return (
            <li key={segment.key} className={state}>
              <span className="step-no" aria-hidden="true">
                {index + 1}
              </span>
              {isPendingSlot ? '待现场处理' : segment.label}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
