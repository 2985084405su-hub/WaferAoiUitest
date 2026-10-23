import { useMemo } from 'react';
import { fmtDuration, stepTags } from '../domain';
import type { StepState } from '../types';
import { StatusBadge } from './Badge';

interface StepListProps {
  steps: StepState[];
  selected: string | null;
  onSelect?: (stepId: string) => void;
  now: number;
  readonly?: boolean;
}

interface StepGroup {
  section: string;
  title: string;
  steps: StepState[];
}

/** 按章节分组是纯计算，不在渲染期改外部变量。 */
function groupBySection(steps: StepState[]): StepGroup[] {
  const groups: StepGroup[] = [];
  for (const step of steps) {
    const last = groups[groups.length - 1];
    if (!last || last.section !== step.section) {
      groups.push({ section: step.section, title: step.sectionTitle, steps: [step] });
    } else {
      last.steps.push(step);
    }
  }
  return groups;
}

export function StepList({ steps, selected, onSelect, now, readonly = false }: StepListProps) {
  const groups = useMemo(() => groupBySection(steps), [steps]);

  return (
    <div>
      <div className="step-head" aria-hidden="true">
        <span>编号</span>
        <span>步骤 / 原因</span>
        <span>状态</span>
        <span className="ta-r">耗时</span>
      </div>
      {groups.map((group) => (
        <div key={group.section}>
          <div className="section-head">
            {group.section} {group.title}
          </div>
          {group.steps.map((step) => {
            const isCurrent = step.status === 'running' || step.status === 'waiting';
            const classes = ['step-row'];
            if (isCurrent) classes.push('is-current');
            if (step.status === 'failed') classes.push('is-failed');
            if (step.status === 'blocked') classes.push('is-blocked');
            if (selected === step.stepId) classes.push('is-selected');

            const liveDuration =
              step.status === 'running' && step.startedAt
                ? now - Date.parse(step.startedAt)
                : step.durationMs;
            const tags = stepTags(step).filter((tag) => tag.tone !== 'hardware');

            return (
              <button
                key={step.stepId}
                type="button"
                data-step={step.stepId}
                className={classes.join(' ')}
                onClick={readonly ? undefined : () => onSelect?.(step.stepId)}
                aria-current={isCurrent ? 'step' : undefined}
                tabIndex={readonly ? -1 : 0}
              >
                <span className="step-id">{step.stepId}</span>
                <span className="step-main">
                  <span className="step-title">{step.title}</span>
                  {step.reason ? <span className="step-reason">{step.reason}</span> : null}
                  {tags.length > 0 ? (
                    <span className="step-tags">
                      {tags.map((tag) => (
                        <span key={tag.text} className={`tag tag-${tag.tone}`}>
                          {tag.text}
                        </span>
                      ))}
                    </span>
                  ) : null}
                </span>
                <span className="step-state">
                  <StatusBadge status={step.status} />
                </span>
                <span className="step-time">{fmtDuration(liveDuration)}</span>
              </button>
            );
          })}
        </div>
      ))}
    </div>
  );
}
