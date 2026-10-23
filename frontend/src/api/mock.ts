/**
 * 模拟后端：仅用于后端路由尚未落地时的页面联调与评审。
 *
 * 它按 RunSnapshot / RunEvent 的真实结构产出数据，并在快照上置 simulated=true，
 * 界面必须显示“模拟运行”。正式联调时 createBackend() 会优先选择 HttpBackend，
 * 这个文件不会参与运行。
 */
import { computeCounts } from '../domain';
import type {
  CheckpointResult,
  CheckpointState,
  ConfigValidationResult,
  ConfigView,
  ConfirmCheckpointRequest,
  ConfirmCheckpointResponse,
  CreateRunRequest,
  CreateRunResponse,
  FieldResolutionRequest,
  FieldResolutionResponse,
  RunEvent,
  RunOptions,
  RunSnapshot,
  RunSummary,
  StepDefinition,
  StepState,
  StepStatus,
} from '../types';
import type { Backend } from './backend';

interface CatalogStep {
  stepId: string;
  title: string;
  section: string;
  sectionTitle: string;
  hardware: 'none' | 'partial' | 'full' | 'manual';
  manualRequirement: 'none' | 'always' | 'hardwarePolicyOnly';
  versionAssertion?: boolean;
  instruction?: string;
  observation: string;
}

/** 抄自 Contracts\RunModels.cs 的 MainFlowCatalog，供模拟使用。 */
const CATALOG: CatalogStep[] = [
  { stepId: '01.01', title: '启动并检查单实例及硬件状态登录窗口', section: '01', sectionTitle: '启动', hardware: 'partial', manualRequirement: 'none', observation: '登录窗口出现、单实例检查通过、硬件状态可在启动流程中观察' },
  { stepId: '01.02', title: '登录后核对软件版本和编译日期并截图', section: '01', sectionTitle: '启动', hardware: 'none', manualRequirement: 'none', versionAssertion: true, observation: '页面显示的版本号与编译日期与本轮安装记录一致' },
  { stepId: '02.01', title: '选择 admin 并登录', section: '02', sectionTitle: '登录', hardware: 'none', manualRequirement: 'none', observation: '选中 admin 并成功进入主界面' },
  { stepId: '02.02', title: '处理启动初始化及回零提示', section: '02', sectionTitle: '登录', hardware: 'full', manualRequirement: 'hardwarePolicyOnly', observation: '回零提示被处理且回零完成，阻断报警有明确状态' },
  { stepId: '03.01', title: '放片及工位空片检查，记录本轮开始时间', section: '03', sectionTitle: '放片', hardware: 'manual', manualRequirement: 'always', instruction: '按现场规程在 Loadport1 槽1 放置晶圆并完成装盒，核对工位无遗留晶圆；完成后在网页确认。', observation: '现场放片完成确认；工位空片状态可观察' },
  { stepId: '03.02', title: '任务晶圆分布页选择 Loadport1', section: '03', sectionTitle: '放片', hardware: 'partial', manualRequirement: 'always', observation: '任务晶圆分布页显示 Loadport1 且选择回读一致' },
  { stepId: '04.01', title: '打开新建配方窗口', section: '04', sectionTitle: '新建配方 A', hardware: 'partial', manualRequirement: 'none', observation: '新建配方窗口打开；平台无片/吸附提示按实际条件处理' },
  { stepId: '04.02', title: '文件类型选配方文件并回读', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '文件类型回读为“配方文件”' },
  { stepId: '04.03', title: '输入带秒级时间戳的配方名称并回读', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '名称含本轮秒级时间戳且回读一致' },
  { stepId: '04.04', title: '创建位置选根目录并回读', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '创建位置回读为“根目录”' },
  { stepId: '04.05', title: '选择常规建档模式并回读', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '建档模式回读为“常规建档模式”' },
  { stepId: '04.06', title: '选择 300 和 Notch 并回读', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '晶圆尺寸 300、缺口类型 Notch 回读一致' },
  { stepId: '04.07', title: '确定新建并检查配方树、当前配方及落盘', section: '04', sectionTitle: '新建配方 A', hardware: 'none', manualRequirement: 'none', observation: '配方树出现新配方、当前配方为 A，且目录落盘' },
  { stepId: '05.01', title: '打开晶圆信息页并选择正面2D检测', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '晶圆信息页打开且检测类型回读为正面2D检测' },
  { stepId: '05.02', title: '输入产品ID和工序ID并回读', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '产品ID、工序ID回读一致' },
  { stepId: '05.03', title: '取消仿真模式', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '仿真模式处于取消状态' },
  { stepId: '05.04', title: '选择注水和清洗次数1', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '介质模式为注水、清洗次数为 1' },
  { stepId: '05.05', title: '输入注水冲洗时间并回读', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '注水冲洗时间回读为 30000' },
  { stepId: '05.06', title: '输入注水后吸水时间并回读', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '注水后吸水时间回读为 30000' },
  { stepId: '05.07', title: '输入吸附晶圆后的吸水时间并回读', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '吸附晶圆后的吸水时间回读为 5000' },
  { stepId: '05.08', title: '输入吸附晶圆的真空时间并回读', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '吸附晶圆的真空时间回读为 60000' },
  { stepId: '05.09', title: '处理风刀字段并保存配方A', section: '05', sectionTitle: '晶圆信息', hardware: 'none', manualRequirement: 'none', observation: '风刀字段处理完成且配方 A 保存成功' },
  { stepId: '06.01', title: '任务晶圆分布页选择Loadport1层1', section: '06', sectionTitle: '建档上片', hardware: 'full', manualRequirement: 'always', observation: 'Loadport1 层1 选择回读一致，槽位与实物一致' },
  { stepId: '06.02', title: '选择关闭读码和对准角0', section: '06', sectionTitle: '建档上片', hardware: 'none', manualRequirement: 'none', observation: '读码方式为关闭读码、对准角为 0' },
  { stepId: '06.03', title: '确认一键上片按钮并交现场执行上片', section: '06', sectionTitle: '建档上片', hardware: 'full', manualRequirement: 'hardwarePolicyOnly', instruction: '点击一键上片后，按原用例要求完成现场确认，再在网页确认现场已执行上片。', observation: '一键上片被触发，符合原用例条件的提示已处理' },
  { stepId: '06.04', title: '等待上片完成并核对平台及来源槽位', section: '06', sectionTitle: '建档上片', hardware: 'full', manualRequirement: 'hardwarePolicyOnly', observation: '平台有片、来源槽位为 Loadport1 层1、机械手状态及报警可核对' },
  { stepId: '06.05', title: '点击配方确认当前配方仍为A', section: '06', sectionTitle: '建档上片', hardware: 'none', manualRequirement: 'none', observation: '当前配方仍为 A' },
];

const nowIso = () => new Date().toISOString();
const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));
const scenario = () =>
  (typeof localStorage !== 'undefined' && localStorage.getItem('wafer.demoScenario')) || 'normal';

interface PendingConfirm {
  checkpointId: string;
  resolve: (result: { result: CheckpointResult; reason: string; note: string }) => void;
}

export class MockBackend implements Backend {
  readonly kind = 'mock' as const;

  private config: ConfigView = {
    exePath: 'D:\\WaferAOI\\WaferAOI.exe',
    recipeDir: 'D:\\GlobalConfig\\Recipe',
    evidenceDir: 'D:\\WaferAoiEvidence',
    expectedVersion: '4.2.1.318',
    buildDate: '2026-10-18 14:30',
    templateRecipeName: '1',
    userName: 'admin',
    configurationRoot: 'D:\\GlobalConfig\\Config',
    options: { skipVersionAssertion: false, hardwarePolicy: 'strict' },
    passwordConfigured: true,
    passwordEnvironmentVariable: 'WAFER_AOI_UI_PASSWORD',
    dataDir: 'C:\\Users\\Public\\AppData\\Local\\WaferAoiUiTestTool',
    updatedAt: nowIso(),
  };

  private run: RunSnapshot | null = null;
  private archive = new Map<string, RunSnapshot>();
  private history: RunSummary[] = [];
  private events: RunEvent[] = [];
  private sequence = 0;
  private listeners = new Set<(event: RunEvent) => void>();
  private pendingConfirm: PendingConfirm | null = null;
  private handledConfirmations = new Map<string, ConfirmCheckpointResponse>();
  private cancelled = false;

  constructor() {
    this.seedHistory();
  }

  /* ---------- 配置 ---------- */

  async getConfig() {
    return { ...this.config };
  }

  async getStepCatalog(): Promise<StepDefinition[]> {
    return CATALOG.map((step) => ({
      stepId: step.stepId,
      title: step.title,
      section: step.section,
      sectionTitle: step.sectionTitle,
      hardwareRelated: step.hardware !== 'none',
      versionAssertion: Boolean(step.versionAssertion),
      manualRequirement: step.manualRequirement,
      manualInstruction: step.instruction || '',
      observation: step.observation,
    }));
  }

  async saveConfig(patch: Partial<ConfigView>) {
    this.config = { ...this.config, ...patch, updatedAt: nowIso() };
    return { ...this.config };
  }

  async validateConfig(skipVersionAssertion?: boolean): Promise<ConfigValidationResult> {
    const skipVersion = skipVersionAssertion ?? this.config.options.skipVersionAssertion;
    const issues = [];
    if (!/\.exe$/i.test(this.config.exePath)) {
      issues.push({ field: 'exePath', code: 'invalid_format', message: '路径必须以 .exe 结尾。' });
    }
    if (!this.config.evidenceDir) {
      issues.push({ field: 'evidenceDir', code: 'required', message: '证据目录不能为空。' });
    }
    if (!skipVersion && !this.config.expectedVersion) {
      issues.push({ field: 'expectedVersion', code: 'required', message: '未勾选跳过版本断言时必填。' });
    }
    if (!skipVersion && !this.config.buildDate) {
      issues.push({ field: 'buildDate', code: 'required', message: '未勾选跳过版本断言时必填。' });
    }
    return {
      valid: issues.length === 0,
      checkedAt: nowIso(),
      issues,
      checks: [
        { id: 'exe', label: '被测 EXE 路径存在且可执行', status: 'ok', detail: this.config.exePath },
        { id: 'evidence', label: '证据目录可写', status: 'ok', detail: this.config.evidenceDir },
        { id: 'process', label: '无同名被测进程', status: 'ok', detail: '' },
        this.config.options.skipVersionAssertion
          ? { id: 'version', label: '已跳过版本断言', status: 'warning', detail: '01.02 将记为跳过' }
          : { id: 'version', label: '预期版本与编译日期已配置', status: 'ok', detail: `${this.config.expectedVersion} / ${this.config.buildDate}` },
        { id: 'session', label: '管理员权限与桌面会话可用', status: 'ok', detail: '' },
      ],
    };
  }

  /* ---------- 运行 ---------- */

  async getCurrentRun() {
    // 必须返回快照副本：真实 HTTP 每次都反序列化成新对象，React 才能看到变化。
    return this.run ? structuredClone(this.run) : null;
  }

  async createRun(request: CreateRunRequest): Promise<CreateRunResponse> {
    if (this.run && !this.isTerminal(this.run)) {
      return { created: false, run: structuredClone(this.run) };
    }
    if (request.skipHardwareSteps && request.manualHardwareSteps) {
      throw new Error('两个硬件选项不能同时选中。');
    }

    const options: RunOptions = request.options ?? this.config.options;
    const stamp = new Date();
    const runId = `RUN-${stamp.getFullYear()}${String(stamp.getMonth() + 1).padStart(2, '0')}${String(stamp.getDate()).padStart(2, '0')}-${String(stamp.getHours()).padStart(2, '0')}${String(stamp.getMinutes()).padStart(2, '0')}${String(stamp.getSeconds()).padStart(2, '0')}`;

    this.run = {
      runId,
      createdAt: nowIso(),
      updatedAt: nowIso(),
      phase: 'created',
      conclusion: 'pending',
      options,
      config: {
        exePath: this.config.exePath,
        recipeDir: this.config.recipeDir,
        evidenceDir: this.config.evidenceDir,
        expectedVersion: this.config.expectedVersion,
        buildDate: this.config.buildDate,
        templateRecipeName: this.config.templateRecipeName,
        userName: this.config.userName,
        configurationRoot: this.config.configurationRoot,
        passwordEnvironmentVariable: this.config.passwordEnvironmentVariable,
        passwordConfigured: this.config.passwordConfigured,
      },
      range: { implemented: 27, designed: 184, unimplemented: 157 },
      steps: CATALOG.map((step) => this.toStepState(step)),
      counts: { passed: 0, manualPassed: 0, skipped: 0, blocked: 0, failed: 0, notRun: 27, waiting: 0, unknown: 0, total: 27 },
      cleanup: { status: 'notStarted', reason: '' },
      pendingField: { active: false, reason: '', processId: 0, note: '', processExitedVerified: false, deviceStateVerified: false, requiredAction: '' },
      executor: { processId: 48216, alive: true, startedAt: nowIso(), lastEventAt: nowIso(), lastSequence: 0 },
      checkpoint: null,
      artifacts: [],
      lastSequence: 0,
      simulated: true,
      lastMessage: '模拟运行已创建。',
    };
    this.events = [];
    this.sequence = 0;
    this.cancelled = false;
    this.emit('runCreated', { message: '运行已创建（模拟），选项已冻结。' });
    void this.simulate();
    return { created: true, run: structuredClone(this.run) };
  }

  async confirm(
    runId: string,
    checkpointId: string,
    request: ConfirmCheckpointRequest,
  ): Promise<ConfirmCheckpointResponse> {
    const cached = this.handledConfirmations.get(`${checkpointId}:${request.requestId}`);
    if (cached) return { ...cached, replayed: true };

    const run = this.run;
    const checkpoint = run?.checkpoint;
    if (!run || run.runId !== runId || !checkpoint || checkpoint.checkpointId !== checkpointId) {
      return {
        accepted: false,
        replayed: false,
        applied: false,
        checkpointId,
        stepId: '',
        status: 'expired',
        message: '该确认点已失效，未改变结果。',
      };
    }
    if (checkpoint.status !== 'waiting') {
      return {
        accepted: false,
        replayed: false,
        applied: false,
        checkpointId,
        stepId: checkpoint.stepId,
        status: checkpoint.status,
        message: '该确认点已结束，提交被拒绝，未改变结果。',
      };
    }
    if ((request.result === 'blocked' || request.result === 'failed') && !request.reason?.trim()) {
      return {
        accepted: false,
        replayed: false,
        applied: false,
        checkpointId,
        stepId: checkpoint.stepId,
        status: checkpoint.status,
        message: '阻塞或失败必须填写原因。',
      };
    }

    const response: ConfirmCheckpointResponse = {
      accepted: true,
      replayed: false,
      applied: true,
      checkpointId,
      stepId: checkpoint.stepId,
      status: 'resolved',
      message: request.result === 'done' ? '现场已操作，继续执行软件断言。' : '已按现场结果结束该步骤。',
    };
    this.handledConfirmations.set(`${checkpointId}:${request.requestId}`, response);

    checkpoint.status = 'resolved';
    checkpoint.result = request.result;
    checkpoint.note = request.note || request.reason || '';
    checkpoint.resolvedAt = nowIso();
    checkpoint.requestId = request.requestId;
    this.emit('checkpointResolved', {
      stepId: checkpoint.stepId,
      checkpointId,
      message: request.reason || response.message,
    });

    this.pendingConfirm?.resolve({
      result: request.result,
      reason: request.reason || '',
      note: request.note || '',
    });
    this.pendingConfirm = null;
    return response;
  }

  async resolvePendingField(runId: string, request: FieldResolutionRequest): Promise<FieldResolutionResponse> {
    const run = this.run;
    if (!run || run.runId !== runId || !run.pendingField.active) {
      return { released: false, message: '当前没有待现场处理的占用。', verifications: [] };
    }
    const verifications = [
      { id: 'process-exited', label: '被测进程已退出', passed: request.processExitedConfirmed, detail: '执行器按 PID 与启动时间核对' },
      { id: 'no-conflict', label: '无冲突被测进程', passed: true, detail: '未发现同名进程' },
      { id: 'device-state', label: '设备状态已确认', passed: request.deviceStateConfirmed, detail: '由现场确认后提交' },
    ];
    const allPassed = verifications.every((item) => item.passed);
    if (!allPassed) {
      return { released: false, message: '核验未通过，占用保持。', verifications };
    }

    run.pendingField = { ...run.pendingField, active: false, note: request.note || '', processExitedVerified: true, deviceStateVerified: true };
    run.phase = 'finished';
    run.cleanup = { status: 'succeeded', reason: '现场处理完成，占用已解除（模拟）' };
    run.finishedAt = nowIso();
    run.executor = { ...run.executor, alive: false, exitCode: 0 };
    this.emit('pendingFieldResolved', { message: '现场处理完成，执行器核验通过。' });
    this.emit('lockReleased', { message: '机器占用已解除。' });
    this.emit('runFinished', { message: '本轮结束（模拟）。' });
    this.archiveRun();
    return { released: true, message: '占用已解除。', verifications };
  }

  async listRuns(): Promise<RunSummary[]> {
    const rows = [...this.history];
    if (this.run && this.isTerminal(this.run) && !rows.some((row) => row.runId === this.run!.runId)) {
      rows.unshift(this.toSummary(this.run));
    }
    return rows;
  }

  async getRun(runId: string): Promise<RunSnapshot> {
    if (this.run?.runId === runId) return structuredClone(this.run);
    const archived = this.archive.get(runId);
    if (!archived) throw new Error(`未找到运行 ${runId}`);
    return structuredClone(archived);
  }

  async getReport(runId: string) {
    const run = this.run?.runId === runId ? this.run : this.archive.get(runId);
    return {
      runId,
      generatedAt: nowIso(),
      simulated: true,
      conclusion: run?.conclusion ?? 'pending',
      cleanup: run?.cleanup ?? null,
      counts: run?.counts ?? null,
      options: run?.options ?? null,
      note: '模拟报告；正式实现读取 report.json。',
    };
  }

  artifactUrl() {
    return '';
  }

  subscribe(
    _runId: string,
    after: number,
    onEvent: (event: RunEvent) => void,
    onState: (state: 'open' | 'lost' | 'replaying') => void,
  ) {
    const listener = (event: RunEvent) => onEvent(event);
    this.listeners.add(listener);
    onState('replaying');
    const backlog = this.events.filter((event) => event.sequence > after);
    setTimeout(() => {
      backlog.forEach(onEvent);
      onState('open');
    }, 60);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /* ---------- 内部 ---------- */

  private seedHistory() {
    const at = new Date(Date.now() - 86_400_000);
    const steps = CATALOG.map((step) => this.toStepState(step));
    steps.forEach((step, index) => {
      step.status = index === 2 ? 'userSkipped' : 'passed';
      step.durationMs = 540 + index * 13;
    });
    const counts = computeCounts(steps);
    const runId = 'RUN-20261022-091204';
    const snapshot: RunSnapshot = {
      runId,
      createdAt: at.toISOString(),
      updatedAt: at.toISOString(),
      finishedAt: at.toISOString(),
      phase: 'finished',
      conclusion: 'passedWithSkips',
      options: { skipVersionAssertion: false, hardwarePolicy: 'strict' },
      config: {
        exePath: this.config.exePath,
        recipeDir: this.config.recipeDir,
        evidenceDir: this.config.evidenceDir,
        expectedVersion: this.config.expectedVersion,
        buildDate: this.config.buildDate,
        templateRecipeName: '1',
        userName: 'admin',
        configurationRoot: this.config.configurationRoot,
        passwordEnvironmentVariable: 'WAFER_AOI_UI_PASSWORD',
        passwordConfigured: true,
      },
      range: { implemented: 27, designed: 184, unimplemented: 157 },
      steps,
      counts,
      cleanup: { status: 'succeeded', reason: '已结束本轮拥有的进程' },
      pendingField: { active: false, reason: '', processId: 0, note: '', processExitedVerified: false, deviceStateVerified: false, requiredAction: '' },
      executor: { processId: 39172, alive: false, lastSequence: 0, exitCode: 0 },
      checkpoint: null,
      artifacts: [],
      lastSequence: 0,
      simulated: true,
      lastMessage: '历史模拟运行。',
    };
    this.archive.set(runId, snapshot);
    this.history.push(this.toSummary(snapshot));
  }

  private toStepState(step: CatalogStep): StepState {
    return {
      stepId: step.stepId,
      title: step.title,
      section: step.section,
      sectionTitle: step.sectionTitle,
      status: 'notRun',
      reason: '',
      startedAt: null,
      finishedAt: null,
      durationMs: null,
      manual: false,
      manualRequirement: step.manualRequirement,
      hardwareRelated: step.hardware !== 'none',
      hardwareSkipped: false,
      versionAssertion: Boolean(step.versionAssertion),
      evidenceIds: [],
      notes: [],
    };
  }

  private isTerminal(run: RunSnapshot) {
    return run.phase === 'finished' || (run.phase === 'pendingField' && !run.pendingField.active);
  }

  private emit(type: RunEvent['type'], patch: Partial<RunEvent> = {}) {
    const run = this.run;
    if (!run) return;
    this.sequence += 1;
    const event: RunEvent = {
      protocolVersion: 1,
      runId: run.runId,
      sequence: this.sequence,
      at: nowIso(),
      type,
      ...patch,
    };
    this.events.push(event);
    run.lastSequence = this.sequence;
    run.updatedAt = event.at;
    run.executor = { ...run.executor, lastEventAt: event.at, lastSequence: this.sequence };
    if (event.message) run.lastMessage = event.message;
    this.listeners.forEach((listener) => listener(event));
  }

  private recount() {
    if (!this.run) return;
    this.run.counts = computeCounts(this.run.steps);
  }

  private requiresCheckpoint(step: StepState, options: RunOptions) {
    if (step.manualRequirement === 'always') return true;
    // 人工策略：所有涉及硬件的步骤都要网页确认（需求为 8 步）。
    return options.hardwarePolicy === 'manual' && step.hardwareRelated;
  }

  private outcome(step: StepState, options: RunOptions): { status: StepStatus; reason: string } {
    if (options.skipVersionAssertion && step.versionAssertion) {
      return { status: 'userSkipped', reason: '本轮选择跳过版本断言（版本号及编译日期）。' };
    }
    if (options.hardwarePolicy === 'skip' && step.hardwareRelated) {
      const catalog = CATALOG.find((item) => item.stepId === step.stepId);
      const full = catalog?.hardware === 'full' || catalog?.hardware === 'manual';
      return full
        ? { status: 'hardwareSkipped', reason: '本轮选择跳过硬件步骤：整步硬件未执行。' }
        : { status: 'passedWithHardwareSkipped', reason: '本轮选择跳过硬件步骤：硬件部分未执行，软件断言通过。' };
    }
    if (scenario() === 'fail' && step.stepId === '04.03') {
      return { status: 'failed', reason: '配方名称回读不一致（模拟断言失败）。' };
    }
    if (scenario() === 'pending' && step.stepId === '06.04') {
      return { status: 'blocked', reason: '硬件状态未确认：无法核对平台有片与来源槽位。' };
    }
    return { status: 'passed', reason: '' };
  }

  private async simulate() {
    const run = this.run;
    if (!run) return;
    const options = run.options;

    run.phase = 'preparing';
    this.emit('runPreparing', { phase: 'preparing', message: '开始启动前检查（模拟）。' });
    await delay(650);
    if (this.cancelled) return;
    this.emit('runPrepared', {
      phase: 'preparing',
      message: '启动前检查完成（模拟，不代表实机条件已满足）。',
      data: { simulated: 'true' },
    });
    run.phase = 'executing';
    this.emit('runStarted', { phase: 'executing', message: '开始执行本期 27 步（模拟）。' });

    for (const step of run.steps) {
      if (this.cancelled) return;
      step.status = 'running';
      step.startedAt = nowIso();
      this.emit('stepStarted', { stepId: step.stepId, phase: 'executing', status: 'running', message: `步骤开始：${step.title}` });
      await delay(280 + Math.random() * 240);
      if (this.cancelled) return;

      const result = this.outcome(step, options);
      if (result.status === 'passed' && this.requiresCheckpoint(step, options)) {
        step.status = 'waiting';
        run.phase = 'waitingHuman';
        this.recount();
        const checkpoint: CheckpointState = {
          // 每轮唯一：真实后端也会给唯一 checkpointId，不能只靠序号（序号每轮重置）。
          checkpointId: `cp-${step.stepId.replace('.', '')}-${Math.random().toString(36).slice(2, 9)}`,
          stepId: step.stepId,
          instruction: CATALOG.find((item) => item.stepId === step.stepId)?.instruction || '按现场规程完成该步骤要求后，在网页确认。',
          createdAt: nowIso(),
          expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
          status: 'waiting',
          result: null,
          note: '',
          resolvedAt: null,
          requestId: '',
        };
        run.checkpoint = checkpoint;
        this.emit('stepWaiting', { stepId: step.stepId, phase: 'waitingHuman', status: 'waiting', checkpointId: checkpoint.checkpointId, message: '等待现场操作确认。' });
        this.emit('checkpointCreated', { stepId: step.stepId, checkpointId: checkpoint.checkpointId, message: checkpoint.instruction });

        const confirmed = await new Promise<{ result: CheckpointResult; reason: string; note: string }>((resolve) => {
          this.pendingConfirm = { checkpointId: checkpoint.checkpointId, resolve };
        });
        if (this.cancelled) return;

        step.reason = confirmed.reason || (confirmed.result === 'done' ? '现场已操作并确认。' : '');
        step.manual = confirmed.result === 'done';
        step.status = confirmed.result === 'done' ? 'operatorConfirmed' : confirmed.result === 'failed' ? 'failed' : 'blocked';
        run.checkpoint = null;
        run.phase = 'executing';
      } else {
        step.status = result.status;
        step.reason = result.reason;
      }

      step.finishedAt = nowIso();
      step.durationMs = step.startedAt ? Date.parse(step.finishedAt) - Date.parse(step.startedAt) : 0;
      this.recount();
      this.emit('stepFinished', {
        stepId: step.stepId,
        phase: 'executing',
        status: step.status,
        reason: step.reason || undefined,
        message: `${step.stepId} ${step.title}`,
      });

      if (step.status === 'failed' || step.status === 'blocked') break;
      await delay(90);
    }

    const counts = computeCounts(run.steps);
    const stopped = counts.failed > 0 || counts.blocked > 0;

    if (scenario() === 'pending') {
      run.phase = 'pendingField';
      run.conclusion = 'pendingField';
      run.pendingField = {
        active: true,
        since: nowIso(),
        reason: '硬件状态未确认：保留被测程序，等待现场处理。',
        processId: 48216,
        note: '',
        processExitedVerified: false,
        deviceStateVerified: false,
        requiredAction: '按现场规程处理设备并关闭本轮被测程序，再提交现场处理完成。',
      };
      run.artifacts.push({
        artifactId: 'art-uia-tree',
        fileName: 'uia-tree.txt',
        kind: 'tree',
        mimeType: 'text/plain',
        size: 4198,
        createdAt: nowIso(),
        stepId: '06.04',
        available: true,
      });
      this.emit('pendingFieldEntered', { phase: 'pendingField', message: '硬件状态未确认：占用保持，禁止新一轮。' });
      this.emit('runFinished', { phase: 'pendingField', message: '待现场处理（模拟）。' });
      this.recount();
      return;
    }

    run.phase = 'cleaning';
    run.cleanup = { status: 'running', startedAt: nowIso(), reason: '' };
    this.emit('runPhase', { phase: 'cleaning', message: '进入清理阶段。' });
    this.emit('cleanupStarted', { phase: 'cleaning', message: '开始清理（模拟）。' });
    await delay(420);
    if (this.cancelled) return;

    run.cleanup = { status: 'succeeded', startedAt: run.cleanup.startedAt, finishedAt: nowIso(), reason: '已结束本轮拥有的进程' };
    this.emit('cleanupFinished', { phase: 'cleaning', message: '清理完成（模拟）。' });

    if (stopped) {
      const failed = run.steps.some((step) => step.status === 'failed');
      run.conclusion = failed ? 'failed' : 'blocked';
    } else {
      run.conclusion = counts.skipped > 0 ? 'passedWithSkips' : 'passed';
    }
    run.phase = 'finished';
    run.finishedAt = nowIso();
    run.executor = { ...run.executor, alive: false, exitCode: 0 };
    this.emit('lockReleased', { message: '机器占用已解除。' });
    this.emit('runFinished', { phase: 'finished', message: '本轮结束（模拟）。' });
    this.recount();
    this.archiveRun();
  }

  private archiveRun() {
    if (!this.run) return;
    this.archive.set(this.run.runId, structuredClone(this.run));
    this.history = [this.toSummary(this.run), ...this.history.filter((row) => row.runId !== this.run!.runId)];
  }

  private toSummary(run: RunSnapshot): RunSummary {
    return {
      runId: run.runId,
      createdAt: run.createdAt,
      finishedAt: run.finishedAt,
      phase: run.phase,
      conclusion: run.conclusion,
      hardwarePolicy: run.options.hardwarePolicy,
      skipVersionAssertion: run.options.skipVersionAssertion,
      cleanupStatus: run.cleanup.status,
      pendingFieldActive: run.pendingField.active,
      simulated: run.simulated,
      counts: run.counts,
    };
  }
}



