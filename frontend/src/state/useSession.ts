/**
 * 运行会话状态。
 *
 * 边界：
 *  - 服务端状态（配置、本轮快照、历史、事件）只由 Backend 提供，组件不自行推导。
 *  - 客户端状态只保留“选项草稿、选中步骤、视图、连接状态”这类界面临时状态。
 *  - 事件只用于触发刷新、补流与日志；步骤结论一律以快照为准，避免前端二次推导。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createBackend, ApiRequestError, type Backend } from '../api/backend';
import { convergedCount } from '../domain';
import type {
  CheckpointResult,
  ConfigValidationResult,
  ConfigView,
  ConfirmCheckpointResponse,
  CreateRunResponse,
  FieldResolutionResponse,
  HardwarePolicy,
  PreparationCheck,
  RunEvent,
  RunSnapshot,
  RunSummary,
  StepDefinition,
  StepState,
  ValidationIssue,
} from '../types';

export interface LogLine {
  at: string;
  level: 'INFO' | 'WARN' | 'ERR';
  text: string;
  stepId: string;
}

export interface ToastMessage {
  id: number;
  text: string;
  tone: 'info' | 'error';
}

type ConnectionState = 'idle' | 'open' | 'lost' | 'replaying';

const EMPTY_CONFIG: ConfigView = {
  exePath: '',
  recipeDir: '',
  evidenceDir: '',
  expectedVersion: '',
  buildDate: '',
  templateRecipeName: '1',
  userName: 'admin',
  configurationRoot: '',
  options: { skipVersionAssertion: false, hardwarePolicy: 'strict' },
  passwordConfigured: false,
  passwordEnvironmentVariable: 'WAFER_AOI_UI_PASSWORD',
  dataDir: '',
  updatedAt: '',
};

/** 只做“是否为空/格式”这类前端能确定的事；存在性与权限由后端准备检查给。 */
export function validateDraft(config: ConfigView): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (!config.exePath.trim()) {
    issues.push({ field: 'exePath', code: 'required', message: '被测程序 EXE 路径不能为空。' });
  } else if (!/\.exe$/i.test(config.exePath.trim())) {
    issues.push({ field: 'exePath', code: 'invalid_format', message: '路径必须以 .exe 结尾。' });
  }
  if (!config.evidenceDir.trim()) {
    issues.push({ field: 'evidenceDir', code: 'required', message: '证据目录不能为空。' });
  }
  if (!config.options.skipVersionAssertion) {
    if (!config.expectedVersion.trim()) {
      issues.push({ field: 'expectedVersion', code: 'required', message: '未勾选“跳过版本断言”时必填。' });
    }
    const buildDate = config.buildDate.trim();
    if (!buildDate) {
      issues.push({ field: 'buildDate', code: 'required', message: '未勾选“跳过版本断言”时必填。' });
    } else if (!/^\d{4}-\d{2}-\d{2}(\s+\d{2}:\d{2})?$/.test(buildDate)) {
      issues.push({ field: 'buildDate', code: 'invalid_format', message: '格式为 yyyy-MM-dd 或 yyyy-MM-dd HH:mm。' });
    }
  }
  return issues;
}

export function useSession() {
  const backendRef = useRef<Backend | null>(null);
  const [ready, setReady] = useState(false);
  const [backendKind, setBackendKind] = useState<'http' | 'mock'>('http');
  const [config, setConfig] = useState<ConfigView>(EMPTY_CONFIG);
  const [configLoaded, setConfigLoaded] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [run, setRun] = useState<RunSnapshot | null>(null);
  const [history, setHistory] = useState<RunSummary[]>([]);
  const [catalog, setCatalog] = useState<StepDefinition[] | null>(null);
  const [logs, setLogs] = useState<LogLine[]>([]);
  const [connection, setConnection] = useState<ConnectionState>('idle');
  const [prepChecks, setPrepChecks] = useState<PreparationCheck[] | null>(null);
  const [validation, setValidation] = useState<ConfigValidationResult | null>(null);
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const [selectedStep, setSelectedStep] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [starting, setStarting] = useState(false);

  const lastSequence = useRef(0);
  const unsubscribe = useRef<(() => void) | null>(null);
  const attachedRunId = useRef<string | null>(null);
  const refreshTimer = useRef<number | null>(null);
  const toastId = useRef(0);

  const showToast = useCallback((text: string, tone: 'info' | 'error' = 'info') => {
    toastId.current += 1;
    const id = toastId.current;
    setToasts((current) => [...current, { id, text, tone }]);
    window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), 3600);
  }, []);

  const appendLog = useCallback((line: LogLine) => {
    setLogs((current) => [...current.slice(-400), line]);
  }, []);

  /* ---------- 初始化 ---------- */

  useEffect(() => {
    let alive = true;
    void (async () => {
      const backend = await createBackend();
      if (!alive) return;
      backendRef.current = backend;
      setBackendKind(backend.kind);
      try {
        const loaded = await backend.getConfig();
        if (!alive) return;
        setConfig(loaded);
        setConfigLoaded(true);
        backend
          .getStepCatalog()
          .then((steps) => {
            if (alive) setCatalog(steps);
          })
          .catch(() => {
            if (alive) setCatalog(null);
          });
        const [current, runs] = await Promise.all([backend.getCurrentRun(), backend.listRuns()]);
        if (!alive) return;
        setHistory(runs);
        if (current) {
          setRun(current);
          setSelectedStep((value) => value ?? current.steps[0]?.stepId ?? null);
        }
      } catch (error) {
        if (alive) showToast(error instanceof Error ? error.message : '加载配置失败', 'error');
      } finally {
        if (alive) setReady(true);
      }
    })();
    return () => {
      alive = false;
      unsubscribe.current?.();
    };
  }, [showToast]);

  /* ---------- 刷新快照 ---------- */

  const refreshRun = useCallback(async () => {
    const backend = backendRef.current;
    if (!backend) return;
    try {
      const snapshot = await backend.getCurrentRun();
      if (snapshot) {
        setRun(snapshot);
        setSelectedStep((value) => value ?? snapshot.steps[0]?.stepId ?? null);
      }
    } catch {
      setConnection('lost');
    }
  }, []);

  const scheduleRefresh = useCallback(() => {
    if (refreshTimer.current) return;
    refreshTimer.current = window.setTimeout(() => {
      refreshTimer.current = null;
      void refreshRun();
    }, 120);
  }, [refreshRun]);

  /* ---------- 事件订阅 ---------- */

  const attach = useCallback(
    (snapshot: RunSnapshot) => {
      const backend = backendRef.current;
      // 换 runId 必须重新订阅，否则会一直听上一轮的事件。
      if (attachedRunId.current === snapshot.runId && unsubscribe.current) return;
      unsubscribe.current?.();
      attachedRunId.current = snapshot.runId;
      if (!backend) return;
      lastSequence.current = snapshot.lastSequence;
      unsubscribe.current = backend.subscribe(
        snapshot.runId,
        snapshot.lastSequence,
        (event: RunEvent) => {
          if (event.sequence <= lastSequence.current) return; // 按 sequence 去重
          lastSequence.current = event.sequence;
          if (event.message) {
            const level: LogLine['level'] =
              event.type === 'stepFinished' && event.status === 'failed'
                ? 'ERR'
                : event.status === 'blocked' || event.type === 'pendingFieldEntered'
                  ? 'WARN'
                  : 'INFO';
            appendLog({ at: event.at, level, text: event.message, stepId: event.stepId || '_run' });
          }
          scheduleRefresh();
        },
        (next) => {
          setConnection((previous) => {
            if (next === 'open' && (previous === 'lost' || previous === 'replaying')) {
              showToast('连接已恢复：按 runId + sequence 补齐，未重新执行步骤。');
            }
            return next;
          });
        },
      );
    },
    [appendLog, scheduleRefresh, showToast],
  );

  /* ---------- 轮询兜底（SSE 不可用时仍能看到状态） ---------- */

  useEffect(() => {
    if (!run || run.phase === 'finished' || (run.phase === 'pendingField' && !run.pendingField.active)) return;
    const timer = window.setInterval(() => {
      if (connection === 'lost') void refreshRun();
    }, 1500);
    return () => window.clearInterval(timer);
  }, [run, connection, refreshRun]);

  /* ---------- 计时 ---------- */

  useEffect(() => {
    const active = run && run.phase !== 'finished';
    if (!active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [run]);

  /* ---------- 配置草稿 ---------- */

  const setField = useCallback((field: keyof ConfigView, value: string) => {
    setConfig((current) => ({ ...current, [field]: value }));
    setDirty(true);
    setValidation(null);
  }, []);

  const toggleOption = useCallback((key: 'skipVersion' | 'skipHardware' | 'manualHardware') => {
    setConfig((current) => {
      const options = { ...current.options };
      if (key === 'skipVersion') {
        options.skipVersionAssertion = !options.skipVersionAssertion;
      } else if (key === 'skipHardware') {
        const next = options.hardwarePolicy !== 'skip';
        options.hardwarePolicy = next ? 'skip' : 'strict';
      } else {
        const next = options.hardwarePolicy !== 'manual';
        options.hardwarePolicy = next ? 'manual' : 'strict';
      }
      return { ...current, options };
    });
    setDirty(true);
    setValidation(null);
  }, []);

  const setHardwarePolicy = useCallback((policy: HardwarePolicy) => {
    setConfig((current) => ({ ...current, options: { ...current.options, hardwarePolicy: policy } }));
    setDirty(true);
  }, []);

  const localIssues = useMemo(() => validateDraft(config), [config]);
  const locked = Boolean(run && (run.phase !== 'finished' || run.pendingField.active));

  const startDisabledReason = useMemo(() => {
    if (run?.pendingField.active) return '待现场处理：占用未释放，禁止新一轮。';
    if (run && run.phase !== 'finished') return '运行中：本机已有一轮执行。';
    if (!configLoaded) return '正在加载本机配置…';
    if (localIssues.length > 0) return `配置校验失败：${localIssues[0].message}（共 ${localIssues.length} 项）`;
    return '';
  }, [run, configLoaded, localIssues]);

  /* ---------- 动作 ---------- */

  const saveConfig = useCallback(async () => {
    const backend = backendRef.current;
    if (!backend) return false;
    if (localIssues.length > 0) {
      showToast(`配置校验失败，未保存：${localIssues[0].message}`, 'error');
      return false;
    }
    try {
      const saved = await backend.saveConfig({
        exePath: config.exePath,
        recipeDir: config.recipeDir,
        evidenceDir: config.evidenceDir,
        expectedVersion: config.expectedVersion,
        buildDate: config.buildDate,
        options: config.options,
      });
      setConfig(saved);
      setDirty(false);
      showToast('配置已保存。');
      return true;
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存配置失败', 'error');
      return false;
    }
  }, [config, localIssues, showToast]);

  const start = useCallback(async () => {
    const backend = backendRef.current;
    if (!backend || starting) return;
    if (startDisabledReason) {
      showToast(startDisabledReason, 'error');
      return;
    }
    setStarting(true);
    try {
      const checked = await backend.validateConfig(config.options.skipVersionAssertion);
      setValidation(checked);
      setPrepChecks(checked.checks);
      if (!checked.valid) {
        showToast(checked.issues[0]?.message || '启动前检查未通过。', 'error');
        return;
      }
      const response: CreateRunResponse = await backend.createRun({
        requestId: crypto.randomUUID(),
        options: config.options,
      });
      setRun(response.run);
      setSelectedStep(response.run.steps[0]?.stepId ?? null);
      setLogs([]);
      lastSequence.current = response.run.lastSequence;
      attach(response.run);
      if (!response.created) {
        showToast('本机已有一轮运行，已切换到该运行（未新建）。', 'error');
      }
    } catch (error) {
      if (error instanceof ApiRequestError) {
        showToast(error.message, 'error');
        void refreshRun();
      } else {
        showToast(error instanceof Error ? error.message : '创建运行失败', 'error');
      }
    } finally {
      setStarting(false);
    }
  }, [attach, config.options, refreshRun, showToast, startDisabledReason, starting]);

  // 运行恢复：刷新页面后若已有未结束运行，重新挂上事件流。
  useEffect(() => {
    if (!run || !ready) return;
    if (attachedRunId.current === run.runId && unsubscribe.current) return;
    attach(run);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, run?.runId]);

  const confirmCheckpoint = useCallback(
    async (result: CheckpointResult, reason: string, note: string): Promise<ConfirmCheckpointResponse | null> => {
      const backend = backendRef.current;
      if (!backend || !run?.checkpoint) return null;
      try {
        const response = await backend.confirm(run.runId, run.checkpoint.checkpointId, {
          requestId: crypto.randomUUID(),
          result,
          reason: reason || undefined,
          note: note || undefined,
        });
        if (!response.accepted) {
          showToast(response.message || '该确认点已失效，未改变结果。', 'error');
          await refreshRun();
          return response;
        }
        if (response.replayed) showToast('该确认已提交过，返回同一结果。');
        else showToast(result === 'done' ? '已提交“完成（现场已操作）”。' : '已提交现场结果。');
        await refreshRun();
        return response;
      } catch (error) {
        showToast(error instanceof Error ? error.message : '提交确认失败', 'error');
        await refreshRun();
        return null;
      }
    },
    [refreshRun, run, showToast],
  );

  const resolvePendingField = useCallback(
    async (
      processExitedConfirmed: boolean,
      deviceStateConfirmed: boolean,
      note: string,
    ): Promise<FieldResolutionResponse | null> => {
      const backend = backendRef.current;
      if (!backend || !run) return null;
      try {
        const response = await backend.resolvePendingField(run.runId, {
          requestId: crypto.randomUUID(),
          processExitedConfirmed,
          deviceStateConfirmed,
          note,
        });
        if (response.released) {
          showToast('现场处理完成，占用已解除。原结论未改写。');
        } else {
          showToast(response.message || '核验未通过，占用保持。', 'error');
        }
        await refreshRun();
        return response;
      } catch (error) {
        showToast(error instanceof Error ? error.message : '提交现场处理失败', 'error');
        return null;
      }
    },
    [refreshRun, run, showToast],
  );

  const refreshHistory = useCallback(async () => {
    const backend = backendRef.current;
    if (!backend) return;
    try {
      setHistory(await backend.listRuns());
    } catch {
      showToast('历史记录加载失败。', 'error');
    }
  }, [showToast]);

  const loadReport = useCallback(async (): Promise<unknown | null> => {
    const backend = backendRef.current;
    if (!backend || !run) return null;
    try {
      return await backend.getReport(run.runId);
    } catch (error) {
      showToast(error instanceof Error ? error.message : '报告读取失败', 'error');
      return null;
    }
  }, [run, showToast]);

  const loadRun = useCallback(async (runId: string) => {
    const backend = backendRef.current;
    if (!backend) return null;
    try {
      return await backend.getRun(runId);
    } catch (error) {
      showToast(error instanceof Error ? error.message : '读取运行失败', 'error');
      return null;
    }
  }, [showToast]);

  /** 未运行时的 27 步清单：来自后端步骤定义，前端不内置步骤表。 */
  const idleSteps = useMemo<StepState[]>(
    () =>
      (catalog ?? []).map((definition) => ({
        stepId: definition.stepId,
        title: definition.title,
        section: definition.section,
        sectionTitle: definition.sectionTitle,
        status: 'notRun',
        reason: '',
        startedAt: null,
        finishedAt: null,
        durationMs: null,
        manual: false,
        manualRequirement: definition.manualRequirement,
        hardwareRelated: definition.hardwareRelated,
        hardwareSkipped: false,
        versionAssertion: definition.versionAssertion,
        evidenceIds: [],
        notes: [],
      })),
    [catalog],
  );

  // 计数以后端快照为准：步骤状态与计数都由事件投影产出，前端再做一次推导会出现两套口径。
  const counts = useMemo(() => (run ? run.counts : null), [run]);
  const converged = useMemo(() => (counts ? convergedCount(counts) : 0), [counts]);

  return {
    ready,
    backendKind,
    config,
    configLoaded,
    dirty,
    setField,
    toggleOption,
    setHardwarePolicy,
    localIssues,
    validation,
    prepChecks,
    locked,
    startDisabledReason,
    saveConfig,
    start,
    starting,
    run,
    counts,
    converged,
    logs,
    connection,
    toasts,
    selectedStep,
    setSelectedStep,
    confirmCheckpoint,
    resolvePendingField,
    history,
    catalog,
    idleSteps,
    refreshHistory,
    loadRun,
    loadReport,
    now,
    showToast,
  };
}

export type Session = ReturnType<typeof useSession>;


