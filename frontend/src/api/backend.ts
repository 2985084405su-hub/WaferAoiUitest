/**
 * 后端访问层。
 *
 * 路由与 backend\src\WaferAoi.UiTestTool.Web\ApiEndpoints.cs 一一对应：
 *   GET  /api/catalog/steps                  -> { implemented, designed, unimplemented, steps }
 *   GET  /api/config                         -> ConfigView
 *   PUT  /api/config                         -> ConfigView
 *   POST /api/config/validate                -> ConfigValidationResult
 *   GET  /api/runs/current                   -> RunSnapshot | null
 *   POST /api/runs                           -> CreateRunResponse
 *   GET  /api/runs?limit&offset              -> { items, total, limit, offset }
 *   GET  /api/runs/{runId}                   -> RunSnapshot
 *   GET  /api/runs/{runId}/events?after=seq  -> SSE（命名事件）
 *   POST /api/runs/{runId}/checkpoints/{id}/confirm -> ConfirmCheckpointResponse
 *   POST /api/runs/{runId}/field-resolution  -> FieldResolutionResponse
 *   GET  /api/runs/{runId}/artifacts/{id}    -> 文件
 */
import type {
  ApiError,
  ConfigValidationResult,
  ConfigView,
  ConfirmCheckpointRequest,
  ConfirmCheckpointResponse,
  CreateRunRequest,
  CreateRunResponse,
  FieldResolutionRequest,
  FieldResolutionResponse,
  RunEvent,
  RunSnapshot,
  RunSummary,
  StepDefinition,
} from '../types';

export interface Backend {
  readonly kind: 'http' | 'mock';
  getConfig(): Promise<ConfigView>;
  /** 27 步静态定义；后端未提供该接口时返回 null，界面降级为空态说明。 */
  getStepCatalog(): Promise<StepDefinition[] | null>;
  saveConfig(patch: Partial<ConfigView>): Promise<ConfigView>;
  validateConfig(skipVersionAssertion: boolean): Promise<ConfigValidationResult>;
  getCurrentRun(): Promise<RunSnapshot | null>;
  createRun(request: CreateRunRequest): Promise<CreateRunResponse>;
  confirm(
    runId: string,
    checkpointId: string,
    request: ConfirmCheckpointRequest,
  ): Promise<ConfirmCheckpointResponse>;
  resolvePendingField(runId: string, request: FieldResolutionRequest): Promise<FieldResolutionResponse>;
  listRuns(): Promise<RunSummary[]>;
  getRun(runId: string): Promise<RunSnapshot>;
  getReport(runId: string): Promise<unknown>;
  artifactUrl(runId: string, artifactId: string): string;
  /**
   * 订阅事件。返回取消订阅函数。
   * onState 用于把传输层状态（连接中断/补齐中）与运行结论分开表达。
   */
  subscribe(
    runId: string,
    after: number,
    onEvent: (event: RunEvent) => void,
    onState: (state: 'open' | 'lost' | 'replaying') => void,
  ): () => void;
}

export class ApiRequestError extends Error {
  readonly status: number;
  readonly code: string;
  readonly issues: ApiError['issues'];

  constructor(status: number, error: ApiError | null, fallback: string) {
    super(error?.message || fallback);
    this.name = 'ApiRequestError';
    this.status = status;
    this.code = error?.code || '';
    this.issues = error?.issues ?? null;
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: { 'content-type': 'application/json', ...(init?.headers || {}) },
  });
  if (!response.ok) {
    let body: ApiError | null = null;
    try {
      body = (await response.json()) as ApiError;
    } catch {
      body = null;
    }
    throw new ApiRequestError(response.status, body, `请求失败（HTTP ${response.status}）`);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/** 与 ApiEndpoints.WireName 一一对应：后端用命名事件推送，EventSource 不会触发 onmessage。 */
const WIRE_EVENT_NAMES = [
  'runCreated',
  'runPreparing',
  'runPrepared',
  'runStarted',
  'runPhase',
  'stepStarted',
  'stepWaiting',
  'stepFinished',
  'stepUnknown',
  'checkpointCreated',
  'checkpointResolved',
  'log',
  'artifactCreated',
  'cleanupStarted',
  'cleanupFinished',
  'pendingFieldEntered',
  'pendingFieldResolved',
  'lockReleased',
  'runFinished',
] as const;

export class HttpBackend implements Backend {
  readonly kind = 'http' as const;

  getConfig() {
    return request<ConfigView>('/api/config');
  }

  async getStepCatalog(): Promise<StepDefinition[] | null> {
    try {
      const payload = await request<{ steps: StepDefinition[] }>('/api/catalog/steps');
      return payload.steps;
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 404) return null;
      throw error;
    }
  }

  saveConfig(patch: Partial<ConfigView>) {
    return request<ConfigView>('/api/config', { method: 'PUT', body: JSON.stringify(patch) });
  }

  validateConfig(skipVersionAssertion: boolean) {
    return request<ConfigValidationResult>('/api/config/validate', {
      method: 'POST',
      body: JSON.stringify({ skipVersionAssertion }),
    });
  }

  getCurrentRun() {
    return request<RunSnapshot | null>('/api/runs/current');
  }

  createRun(body: CreateRunRequest) {
    return request<CreateRunResponse>('/api/runs', { method: 'POST', body: JSON.stringify(body) });
  }

  confirm(runId: string, checkpointId: string, body: ConfirmCheckpointRequest) {
    return request<ConfirmCheckpointResponse>(
      `/api/runs/${encodeURIComponent(runId)}/checkpoints/${encodeURIComponent(checkpointId)}/confirm`,
      { method: 'POST', body: JSON.stringify(body) },
    );
  }

  resolvePendingField(runId: string, body: FieldResolutionRequest) {
    return request<FieldResolutionResponse>(
      `/api/runs/${encodeURIComponent(runId)}/field-resolution`,
      { method: 'POST', body: JSON.stringify(body) },
    );
  }

  async listRuns() {
    const payload = await request<{ items: RunSummary[] }>('/api/runs?limit=50');
    return payload.items;
  }

  getRun(runId: string) {
    return request<RunSnapshot>(`/api/runs/${encodeURIComponent(runId)}`);
  }

  getReport(runId: string) {
    return request<unknown>(`/api/runs/${encodeURIComponent(runId)}/report`);
  }

  artifactUrl(runId: string, artifactId: string) {
    return `/api/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactId)}`;
  }

  subscribe(
    runId: string,
    after: number,
    onEvent: (event: RunEvent) => void,
    onState: (state: 'open' | 'lost' | 'replaying') => void,
  ) {
    const url = `/api/runs/${encodeURIComponent(runId)}/events?after=${after}`;
    const source = new EventSource(url);

    source.onopen = () => onState('open');
    source.onerror = () => onState('lost');

    const handle = (message: Event) => {
      const data = (message as MessageEvent<string>).data;
      if (!data) return;
      try {
        onEvent(JSON.parse(data) as RunEvent);
      } catch {
        // 单条坏事件不打断整体流；按序号去重由调用方负责。
      }
    };

    source.onmessage = handle;
    // 后端发的是 `event: runCreated` 这类命名事件，必须逐个注册，否则收不到任何事件。
    for (const name of WIRE_EVENT_NAMES) {
      source.addEventListener(name, handle);
    }

    return () => source.close();
  }
}

/**
 * 探测后端是否可用；不可用时切到明确标注的模拟后端，避免页面白屏。
 * 正式联调时后端一定可用，这条分支不会触发。
 */
export async function createBackend(): Promise<Backend> {
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 1500);
    const response = await fetch('/api/config', { signal: controller.signal });
    clearTimeout(timer);
    // 静态托管（没有后端）时 /api/config 可能被回落到 index.html，必须校验 JSON 才能认定后端可用。
    const contentType = response.headers.get('content-type') || '';
    if (response.ok && contentType.includes('application/json')) return new HttpBackend();
  } catch {
    // 走模拟分支
  }
  const { MockBackend } = await import('./mock');
  return new MockBackend();
}
