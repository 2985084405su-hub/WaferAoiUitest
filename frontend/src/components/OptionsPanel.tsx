import { policyText } from '../domain';
import type { Session } from '../state/useSession';
import { OptionRow } from './OptionRow';

/** 三个运行选项：默认全不选，两个硬件项互斥且可清空。 */
export function OptionsPanel({ session }: { session: Session }) {
  const { config, toggleOption, locked, start, starting, startDisabledReason, run } = session;
  const options = config.options;

  return (
    <section className="card" aria-labelledby="options-title">
      <div className="card-head">
        <h2 id="options-title">运行选项</h2>
        <span className="muted">开始后冻结</span>
      </div>
      <fieldset className="options" disabled={locked}>
        <legend className="sr-only">运行选项</legend>
        <OptionRow
          id="optSkipVersion"
          title="跳过版本断言"
          description="01.02 记为跳过；版本号与编译日期不再作为启动必填条件。"
          checked={options.skipVersionAssertion}
          disabled={locked}
          onChange={() => toggleOption('skipVersion')}
        />
        <OptionRow
          id="optSkipHardware"
          title="跳过硬件步骤"
          description="整步硬件记为跳过；混合步骤仍执行可独立完成的软件断言。"
          checked={options.hardwarePolicy === 'skip'}
          disabled={locked}
          onChange={() => toggleOption('skipHardware')}
        />
        <OptionRow
          id="optManualHardware"
          title="人工执行硬件步骤"
          description="硬件操作点等待网页确认后继续，仍执行软件断言。"
          checked={options.hardwarePolicy === 'manual'}
          disabled={locked}
          onChange={() => toggleOption('manualHardware')}
        />
      </fieldset>
      <p className="policy">{policyText(options)}</p>
      <button
        id="btn-start"
        type="button"
        className="btn btn-primary"
        onClick={() => void start()}
        disabled={Boolean(startDisabledReason) || starting}
        aria-busy={starting}
      >
        {starting ? '启动中…' : run && run.phase !== 'finished' ? '执行中…' : '开始执行'}
      </button>
      <p className={`hint${startDisabledReason ? ' is-warn' : ''}`} id="start-hint">
        {startDisabledReason || '配置校验通过，可以开始。'}
      </p>
    </section>
  );
}

