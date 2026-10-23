import type { ReactNode } from 'react';
import { STATUS_META } from '../domain';
import type { StepStatus } from '../types';

export type Tone = 'neutral' | 'running' | 'waiting' | 'pass' | 'skip' | 'blocked' | 'failed' | 'unknown';

interface BadgeProps {
  tone: Tone;
  children: ReactNode;
  spinning?: boolean;
  title?: string;
}

/** 颜色永远与中文同时出现，不允许只靠颜色表达状态。 */
export function Badge({ tone, children, spinning = false, title }: BadgeProps) {
  return (
    <span className={`badge b-${tone}`} title={title}>
      {spinning ? <span className="spin" aria-hidden="true" /> : <span className="dot" aria-hidden="true" />}
      {children}
    </span>
  );
}

export function StatusBadge({ status }: { status: StepStatus }) {
  const meta = STATUS_META[status];
  return (
    <Badge tone={meta.tone} spinning={meta.spinning}>
      {meta.label}
    </Badge>
  );
}
