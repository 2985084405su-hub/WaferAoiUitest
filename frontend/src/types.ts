/**
 * 后端契约类型（与 backend\src\WaferAoi.UiTestTool.Contracts 的 JSON 输出一致）。
 * 约定：camelCase、字符串枚举（EnumJsonConverter 用 camelCase）、null 省略。
 * 前端只消费这些字段，不做二次推导。
 */

export type StepStatus =
  | 'notRun'
  | 'running'
  | 'waiting'
  | 'passed'
  | 'operatorConfirmed'
  | 'passedWithHardwareSkipped'
  | 'hardwareSkipped'
  | 'userSkipped'
  | 'blocked'
  | 'failed'
  | 'unknown';

export type HardwarePolicy = 'strict' | 'skip' | 'manual';

export type RunPhase =
  | 'created'
  | 'preparing'
  | 'executing'
  | 'waitingHuman'
  | 'cleaning'
  | 'pendingField'
  | 'finished';

export type RunConclusion =
  | 'pending'
  | 'passed'
  | 'passedWithSkips'
  | 'blocked'
  | 'failed'
  | 'interrupted'
  | 'pendingField';

export type CleanupStatus = 'notStarted' | 'notRequired' | 'running' | 'succeeded' | 'failed';

export type CheckpointStatus = 'waiting' | 'resolved' | 'expired';

export type CheckpointResult = 'done' | 'blocked' | 'failed';

export type EventType =
  | 'runCreated'
  | 'runPreparing'
  | 'runPrepared'
  | 'runStarted'
  | 'runPhase'
  | 'stepStarted'
  | 'stepWaiting'
  | 'stepFinished'
  | 'stepUnknown'
  | 'checkpointCreated'
  | 'checkpointResolved'
  | 'log'
  | 'artifactCreated'
  | 'cleanupStarted'
  | 'cleanupFinished'
  | 'pendingFieldEntered'
  | 'pendingFieldResolved'
  | 'lockReleased'
  | 'runFinished';

export interface RunOptions {
  skipVersionAssertion: boolean;
  hardwarePolicy: HardwarePolicy;
}

/**
 * 步骤静态定义（manifest 里的 StepDefinition）。
 * 快照里的 steps[] 是运行态，缺 title 之外的观测点/人工说明时以这里为准。
 */
export interface StepDefinition {
  stepId: string;
  title: string;
  section: string;
  sectionTitle: string;
  hardwareRelated: boolean;
  versionAssertion: boolean;
  manualRequirement: 'none' | 'always' | 'hardwarePolicyOnly';
  manualInstruction: string;
  observation: string;
}

export interface StepState {
  stepId: string;
  title: string;
  section: string;
  sectionTitle: string;
  status: StepStatus;
  reason: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  durationMs?: number | null;
  manual: boolean;
  manualRequirement: 'none' | 'always' | 'hardwarePolicyOnly';
  hardwareRelated: boolean;
  hardwareSkipped: boolean;
  versionAssertion: boolean;
  evidenceIds: string[];
  notes: string[];
}

export interface StepCounts {
  passed: number;
  manualPassed: number;
  skipped: number;
  blocked: number;
  failed: number;
  notRun: number;
  waiting: number;
  unknown: number;
  total: number;
}

export interface CleanupState {
  status: CleanupStatus;
  startedAt?: string | null;
  finishedAt?: string | null;
  reason: string;
}

export interface PendingFieldState {
  active: boolean;
  since?: string | null;
  reason: string;
  processId: number;
  processStartedAt?: string | null;
  note: string;
  processExitedVerified: boolean;
  deviceStateVerified: boolean;
  requiredAction: string;
}

export interface CheckpointState {
  checkpointId: string;
  stepId: string;
  instruction: string;
  createdAt: string;
  expiresAt: string;
  status: CheckpointStatus;
  result?: CheckpointResult | null;
  note: string;
  resolvedAt?: string | null;
  requestId: string;
}

export interface ExecutorState {
  processId: number;
  alive: boolean;
  startedAt?: string | null;
  lastEventAt?: string | null;
  lastSequence: number;
  exitCode?: number | null;
}

export interface ArtifactInfo {
  artifactId: string;
  fileName: string;
  kind: string;
  mimeType: string;
  size: number;
  createdAt: string;
  stepId: string;
  available: boolean;
}

export interface RunConfigSnapshot {
  exePath: string;
  recipeDir: string;
  evidenceDir: string;
  expectedVersion: string;
  buildDate: string;
  templateRecipeName: string;
  userName: string;
  configurationRoot: string;
  passwordEnvironmentVariable: string;
  passwordConfigured: boolean;
}

export interface RangeInfo {
  implemented: number;
  designed: number;
  unimplemented: number;
}

export interface RunSnapshot {
  runId: string;
  createdAt: string;
  updatedAt: string;
  finishedAt?: string | null;
  phase: RunPhase;
  conclusion: RunConclusion;
  options: RunOptions;
  config: RunConfigSnapshot;
  range: RangeInfo;
  steps: StepState[];
  counts: StepCounts;
  cleanup: CleanupState;
  pendingField: PendingFieldState;
  executor: ExecutorState;
  checkpoint?: CheckpointState | null;
  artifacts: ArtifactInfo[];
  lastSequence: number;
  simulated: boolean;
  lastMessage: string;
}

export interface ConfigView {
  exePath: string;
  recipeDir: string;
  evidenceDir: string;
  expectedVersion: string;
  buildDate: string;
  templateRecipeName: string;
  userName: string;
  configurationRoot: string;
  options: RunOptions;
  passwordConfigured: boolean;
  passwordEnvironmentVariable: string;
  dataDir: string;
  updatedAt: string;
}

export interface ValidationIssue {
  field: string;
  code: string;
  message: string;
}

export interface PreparationCheck {
  id: string;
  label: string;
  status: 'ok' | 'warning' | 'failed' | string;
  detail: string;
}

export interface ConfigValidationResult {
  valid: boolean;
  checkedAt: string;
  issues: ValidationIssue[];
  checks: PreparationCheck[];
}

export interface RunEvent {
  protocolVersion: number;
  runId: string;
  sequence: number;
  at: string;
  type: EventType;
  stepId?: string | null;
  phase?: RunPhase | null;
  status?: StepStatus | null;
  reason?: string | null;
  checkpointId?: string | null;
  artifactId?: string | null;
  artifactName?: string | null;
  artifactKind?: string | null;
  mimeType?: string | null;
  artifactSize?: number | null;
  message?: string | null;
  data?: Record<string, string> | null;
}

export interface CreateRunRequest {
  requestId: string;
  options?: RunOptions;
  note?: string;
  skipHardwareSteps?: boolean;
  manualHardwareSteps?: boolean;
}

export interface CreateRunResponse {
  created: boolean;
  run: RunSnapshot;
}

export interface ConfirmCheckpointRequest {
  requestId: string;
  result: CheckpointResult;
  reason?: string;
  note?: string;
}

export interface ConfirmCheckpointResponse {
  accepted: boolean;
  replayed: boolean;
  applied: boolean;
  checkpointId: string;
  stepId: string;
  status: CheckpointStatus;
  message: string;
}

export interface FieldResolutionRequest {
  requestId: string;
  processExitedConfirmed: boolean;
  deviceStateConfirmed: boolean;
  note?: string;
}

export interface FieldVerification {
  id: string;
  label: string;
  passed: boolean;
  detail: string;
}

export interface FieldResolutionResponse {
  released: boolean;
  message: string;
  verifications: FieldVerification[];
}

export interface RunSummary {
  runId: string;
  createdAt: string;
  finishedAt?: string | null;
  phase: RunPhase;
  conclusion: RunConclusion;
  hardwarePolicy: HardwarePolicy;
  skipVersionAssertion: boolean;
  cleanupStatus: CleanupStatus;
  pendingFieldActive: boolean;
  simulated: boolean;
  counts: StepCounts;
}

export interface ApiError {
  code: string;
  message: string;
  currentRun?: RunSnapshot | null;
  issues?: ValidationIssue[] | null;
}
