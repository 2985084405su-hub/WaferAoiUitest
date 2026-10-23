import type { Session } from '../state/useSession';
import { TextField } from './TextField';

/** 运行配置：运行中只读，改动只作用于下一轮。 */
export function ConfigPanel({ session }: { session: Session }) {
  const { config, setField, localIssues, locked, dirty, saveConfig, configLoaded } = session;
  const errorOf = (field: string) => localIssues.find((issue) => issue.field === field)?.message;
  const versionRequired = !config.options.skipVersionAssertion;

  return (
    <section className="card" aria-labelledby="config-title">
      <div className="card-head">
        <h2 id="config-title">运行配置</h2>
        <span className="muted">
          {locked ? '运行中只读（本轮快照）' : dirty ? '有未保存修改' : configLoaded ? '已加载本机配置' : '加载中…'}
        </span>
      </div>
      <form className="form" onSubmit={(event) => event.preventDefault()} noValidate>
        <TextField
          id="exePath"
          label="被测程序 EXE 路径"
          value={config.exePath}
          onChange={(value) => setField('exePath', value)}
          error={errorOf('exePath')}
          required
          mono
          disabled={locked}
          spellCheck={false}
        />
        <TextField
          id="recipeDir"
          label="配方目录"
          value={config.recipeDir}
          onChange={(value) => setField('recipeDir', value)}
          error={errorOf('recipeDir')}
          mono
          disabled={locked}
          spellCheck={false}
        />
        <TextField
          id="evidenceDir"
          label="证据目录"
          value={config.evidenceDir}
          onChange={(value) => setField('evidenceDir', value)}
          error={errorOf('evidenceDir')}
          required
          mono
          disabled={locked}
          spellCheck={false}
        />
        <TextField
          id="expectedVersion"
          label="预期版本号"
          value={config.expectedVersion}
          onChange={(value) => setField('expectedVersion', value)}
          error={versionRequired ? errorOf('expectedVersion') : undefined}
          required={versionRequired}
          mono
          disabled={locked}
          spellCheck={false}
        />
        <TextField
          id="buildDate"
          label="预期编译日期"
          value={config.buildDate}
          onChange={(value) => setField('buildDate', value)}
          error={versionRequired ? errorOf('buildDate') : undefined}
          required={versionRequired}
          mono
          disabled={locked}
          placeholder="yyyy-MM-dd 或 yyyy-MM-dd HH:mm"
          spellCheck={false}
        />
        <div className="field field-fixed">
          <span className="fixed-label">固定业务输入</span>
          <p className="fixed-value">
            模板配方 <code>{config.templateRecipeName || '1'}</code> · 登录账号{' '}
            <code>{config.userName || 'admin'}</code> · 密码{' '}
            <code>{config.passwordConfigured ? '已配置' : '未配置'}</code>
          </p>
        </div>
        <button
          id="btn-save"
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => void saveConfig()}
          disabled={locked}
        >
          保存配置
        </button>
      </form>
    </section>
  );
}

