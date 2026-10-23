/**
 * 后端枚举 → 界面文案/颜色的唯一映射处。
 * 规则：只做映射，不从日志或耗时推导状态；颜色永远与中文同时出现。
 */
import type {
  CleanupStatus,
  HardwarePolicy,
  RunConclusion,
  RunOptions,
  RunPhase,
  StepCounts,
  StepState,
  StepStatus,
} from './types';

export interface StatusMeta {
  label: string;
  tone: 'neutral' | 'running' | 'waiting' | 'pass' | 'skip' | 'blocked' | 'failed' | 'unknown';
  spinning?: boolean;
}

export const STATUS_META: Record<StepStatus, StatusMeta> = {
  notRun: { label: '未执行', tone: 'neutral' },
  running: { label: '执行中', tone: 'running', spinning: true },
  waiting: { label: '等待人工确认', tone: 'waiting' },
  passed: { label: '成功', tone: 'pass' },
  operatorConfirmed: { label: '成功·人工确认', tone: 'pass' },
  passedWithHardwareSkipped: { label: '跳过·部分硬件', tone: 'skip' },
  hardwareSkipped: { label: '跳过·整步硬件', tone: 'skip' },
  userSkipped: { label: '跳过·明确要求', tone: 'skip' },
  blocked: { label: '阻塞', tone: 'blocked' },
  failed: { label: '失败', tone: 'failed' },
  unknown: { label: '中断·结果未知', tone: 'unknown' },
};

export const CONCLUSION_META: Record<RunConclusion, { label: string; tone: StatusMeta['tone'] }> = {
  pending: { label: '尚未形成结论', tone: 'neutral' },
  passed: { label: '通过', tone: 'pass' },
  passedWithSkips: { label: '通过（含跳过）', tone: 'skip' },
  blocked: { label: '未通过·阻塞', tone: 'blocked' },
  failed: { label: '未通过·失败', tone: 'failed' },
  interrupted: { label: '中断·结果未知', tone: 'unknown' },
  pendingField: { label: '待现场处理', tone: 'blocked' },
};

export const CLEANUP_META: Record<CleanupStatus, string> = {
  notStarted: '未开始',
  notRequired: '无需清理',
  running: '清理中',
  succeeded: '清理成功',
  failed: '清理失败',
};

export const PHASE_LABEL: Record<RunPhase, string> = {
  created: '已创建',
  preparing: '准备',
  executing: '执行',
  waitingHuman: '等待人工',
  cleaning: '清理',
  pendingField: '待现场处理',
  finished: '结束',
};

export const POLICY_LABEL: Record<HardwarePolicy, string> = {
  strict: '严格执行',
  skip: '跳过硬件步骤',
  manual: '人工执行硬件步骤',
};

export function computeCounts(steps: StepState[]): StepCounts {
  const counts: StepCounts = {
    passed: 0,
    manualPassed: 0,
    skipped: 0,
    blocked: 0,
    failed: 0,
    notRun: 0,
    waiting: 0,
    unknown: 0,
    total: steps.length,
  };
  for (const step of steps) {
    switch (step.status) {
      case 'passed':
        counts.passed += 1;
        break;
      case 'operatorConfirmed':
        counts.passed += 1;
        counts.manualPassed += 1;
        break;
      case 'passedWithHardwareSkipped':
      case 'hardwareSkipped':
      case 'userSkipped':
        counts.skipped += 1;
        break;
      case 'blocked':
        counts.blocked += 1;
        break;
      case 'failed':
        counts.failed += 1;
        break;
      case 'waiting':
        counts.waiting += 1;
        break;
      case 'unknown':
        counts.unknown += 1;
        break;
      default:
        counts.notRun += 1;
    }
  }
  return counts;
}

/** 已收敛 = 有终态的步骤；等待与中断不计入。 */
export function convergedCount(counts: StepCounts): number {
  return counts.passed + counts.skipped + counts.blocked + counts.failed;
}

export function policyText(options: RunOptions): string {
  const { skipVersionAssertion: sv, hardwarePolicy: hw } = options;
  if (sv && hw === 'skip') {
    return '跳过版本断言 + 跳过硬件步骤：01.02 记为跳过；整步硬件跳过，混合步骤保留软件断言结果。';
  }
  if (sv && hw === 'manual') {
    return '跳过版本断言 + 人工执行硬件步骤：01.02 记为跳过；硬件操作点等待网页确认后继续。';
  }
  if (sv) {
    return '跳过版本断言：01.02 记为跳过；版本号与编译日期的缺失不阻塞启动，硬件仍严格执行。';
  }
  if (hw === 'skip') {
    return '跳过硬件步骤：版本号与编译日期照常核验；整步硬件记为跳过，混合步骤保留软件断言结果。';
  }
  if (hw === 'manual') {
    return '人工执行硬件步骤：版本核验照常；硬件操作点等待网页确认后继续，仍执行软件断言。';
  }
  return '严格执行：版本号与编译日期核验、硬件操作与断言全部按原步骤执行。';
}

export function optionSnapshotText(options: RunOptions): string {
  return `${POLICY_LABEL[options.hardwarePolicy]}；版本断言：${
    options.skipVersionAssertion ? '跳过（01.02 记为跳过）' : '核验'
  }`;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function fmtClock(value?: string | Date | null): string {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export function fmtDateTime(value?: string | Date | null): string {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${fmtClock(d)}`;
}

export function fmtDuration(ms?: number | null): string {
  if (ms == null || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  return `${Math.floor(ms / 60_000)}m${pad(Math.round((ms % 60_000) / 1000))}s`;
}

export function fmtBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export interface StepTag {
  text: string;
  tone: 'manual' | 'order' | 'hardware';
}

/** 标签对应 WEB-11 的界面可读性要求，不能删除。 */
export function stepTags(step: StepState): StepTag[] {
  const tags: StepTag[] = [];
  if (step.stepId === '03.01') tags.push({ text: '原用例人工点', tone: 'manual' });
  if (step.stepId === '01.02') tags.push({ text: '登录后执行', tone: 'order' });
  if (step.hardwareRelated) tags.push({ text: '含硬件部分', tone: 'hardware' });
  return tags;
}
