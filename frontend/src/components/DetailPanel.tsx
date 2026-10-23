import { fmtBytes, fmtClock, stepTags } from '../domain';
import type { Session } from '../state/useSession';
import type { ArtifactInfo, RunSnapshot, StepDefinition, StepState } from '../types';
import { StatusBadge } from './Badge';

function LogView({ lines, stepId }: { lines: Session['logs']; stepId: string }) {
  const scoped = lines.filter((line) => line.stepId === stepId);
  return (
    <section className="card" aria-labelledby="log-title">
      <div className="card-head">
        <h2 id="log-title">本步日志</h2>
        <span className="muted">{scoped.length} 条</span>
      </div>
      <div className="log" role="log" aria-label="本步日志">
        {scoped.length === 0 ? (
          <p className="log-empty">本步尚无日志。</p>
        ) : (
          scoped.map((line, index) => (
            <div className="log-line" key={`${line.at}-${index}`}>
              <time>{fmtClock(line.at)}</time>
              <span className={`lv-${line.level.toLowerCase()}`}>{line.level}</span>
              <span>{line.text}</span>
            </div>
          ))
        )}
      </div>
      <p className="hint">只展示结构化事件与本步日志，不解析中文日志推断状态。</p>
    </section>
  );
}

function ArtifactCard({ artifact, simulated }: { artifact: ArtifactInfo; simulated: boolean }) {
  const previewable = artifact.mimeType.startsWith('image/') || artifact.mimeType.includes('json') || artifact.mimeType.startsWith('text/');
  return (
    <div className={`ev${artifact.available ? '' : ' is-missing'}`} title={artifact.fileName}>
      <b>{artifact.fileName}</b>
      <span>
        {artifact.available
          ? `${previewable ? '可预览' : '格式不支持'} · ${fmtBytes(artifact.size)}`
          : '缺失 · 文件已被移动或删除'}
      </span>
      {artifact.available ? (
        <span className="muted">{simulated ? '模拟数据无下载' : '按产物接口读取'}</span>
      ) : null}
    </div>
  );
}

interface DetailPanelProps {
  run: RunSnapshot | null;
  step: StepState | null;
  definition?: StepDefinition | null;
  logs: Session['logs'];
}

export function DetailPanel({ run, step, definition, logs }: DetailPanelProps) {
  if (!step) {
    return (
      <section className="card">
        <div className="card-head">
          <h2>步骤详情</h2>
        </div>
        <p className="empty">选择左侧步骤查看详情。</p>
      </section>
    );
  }

  const artifacts = run?.artifacts.filter((artifact) => artifact.stepId === step.stepId) ?? [];
  const tags = stepTags(step);

  return (
    <>
      <section className="card" aria-labelledby="detail-title">
        <div className="card-head">
          <h2 id="detail-title">步骤详情</h2>
          <StatusBadge status={step.status} />
        </div>
        <p className="detail-title">
          {step.stepId} {step.title}
        </p>
        <dl className="kv kv-tight">
          <dt>章节</dt>
          <dd>
            {step.section} {step.sectionTitle}
          </dd>
          <dt>编号顺序</dt>
          <dd>
            {step.stepId === '01.02'
              ? '编号在 01 节，实际在登录后执行，可能晚于 02.01 出结果'
              : '按编号顺序'}
          </dd>
        </dl>
        {tags.length > 0 ? (
          <p className="step-tags">
            {tags.map((tag) => (
              <span key={tag.text} className={`tag tag-${tag.tone}`}>
                {tag.text}
              </span>
            ))}
          </p>
        ) : null}

        <p className="cp-label">能力说明</p>
        <p className="detail-text">{describeStep(step)}</p>

        {definition?.observation ? (
          <>
            <p className="cp-label">观测点</p>
            <p className="detail-text">{definition.observation}</p>
          </>
        ) : null}

        {step.manualRequirement !== 'none' ? (
          <>
            <p className="cp-label">现场操作说明</p>
            <p className="detail-text">
              {definition?.manualInstruction ||
                (step.manualRequirement === 'always'
                  ? '原用例明确要求现场参与，网页确认后仍需执行软件断言。'
                  : '仅在“人工执行硬件步骤”策略下等待网页确认。')}
            </p>
          </>
        ) : null}

        {step.reason ? (
          <>
            <p className="cp-label">原因</p>
            <p className="detail-text detail-reason">{step.reason}</p>
          </>
        ) : null}
      </section>

      <LogView lines={logs} stepId={step.stepId} />

      <section className="card" aria-labelledby="evidence-title">
        <div className="card-head">
          <h2 id="evidence-title">证据</h2>
          <span className="muted">按产物接口读取</span>
        </div>
        {artifacts.length === 0 ? (
          <p className="empty">本步尚未生成证据。</p>
        ) : (
          <div className="evidence">
            {artifacts.map((artifact) => (
              <ArtifactCard key={artifact.artifactId} artifact={artifact} simulated={Boolean(run?.simulated)} />
            ))}
          </div>
        )}
      </section>
    </>
  );
}

function describeStep(step: StepState): string {
  if (step.hardwareRelated && step.manualRequirement === 'always') {
    return '含硬件部分；原用例人工点，现场完成后在网页确认，软件断言仍执行。';
  }
  if (step.hardwareRelated) {
    return '含硬件部分；严格策略下自动执行并断言，跳过策略下记为跳过，人工策略下等待网页确认。';
  }
  return '纯软件步骤；按原用例执行操作与回读断言。';
}
