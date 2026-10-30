import { useEffect, useState } from 'react';
import { Badge } from './components/Badge';
import { Banner } from './components/Banner';
import { Toasts } from './components/Toasts';
import { rangeNoteText } from './domain';
import { useSession } from './state/useSession';
import { CurrentRunView } from './views/CurrentRunView';
import { HistoryDetailView } from './views/HistoryDetailView';
import { HistoryView } from './views/HistoryView';

type View = 'current' | 'history' | 'history-detail';

export default function App() {
  const session = useSession();
  const [view, setView] = useState<View>('current');
  const [historyRunId, setHistoryRunId] = useState('');
  const [scenario, setScenario] = useState(
    () => (typeof localStorage !== 'undefined' ? localStorage.getItem('wafer.demoScenario') : null) || 'normal',
  );

  useEffect(() => {
    if (session.backendKind !== 'mock') return;
    localStorage.setItem('wafer.demoScenario', scenario);
  }, [scenario, session.backendKind]);

  const { run, connection, range, sectionRange } = session;
  const lockLabel = run?.pendingField.active
    ? '占用未释放'
    : run && run.phase !== 'finished'
      ? '本轮占用中'
      : '本机空闲';
  const lockTone = run?.pendingField.active ? 'blocked' : run && run.phase !== 'finished' ? 'waiting' : 'neutral';

  const connectionTone = connection === 'lost' ? 'blocked' : connection === 'replaying' ? 'waiting' : 'pass';
  const connectionLabel =
    connection === 'lost' ? '状态连接中断' : connection === 'replaying' ? '正在补齐事件…' : '状态通道正常';

  const openHistory = (runId: string) => {
    setHistoryRunId(runId);
    setView('history-detail');
  };

  return (
    <>
      <a className="skip" href="#steps-title">
        跳到步骤清单
      </a>

      {session.backendKind === 'mock' ? (
        <div className="dev-strip" role="region" aria-label="模拟数据控制（正式联调后自动消失）">
          <span className="dev-tag">模拟数据</span>
          <span className="dev-note">
            后端未连接，当前为 simulated 模拟运行，不代表实机结果。
          </span>
          <label className="dev-field">
            分支
            <select id="demo-scenario" value={scenario} onChange={(event) => setScenario(event.target.value)}>
              <option value="normal">正常收敛</option>
              <option value="fail">失败停止（04.03）</option>
              <option value="pending">待现场处理（06.04）</option>
            </select>
          </label>
        </div>
      ) : null}

      <header className="topbar">
        <div className="brand">
          <h1>主流程自动化回归</h1>
          <Badge tone="neutral">
            {range ? `本期 ${range.implemented} 步 / 完整 ${range.designed} 步` : '范围待加载'}
          </Badge>
        </div>
        <div className="topmeta">
          <Badge tone={lockTone}>{lockLabel}</Badge>
          <Badge tone={connectionTone}>{connectionLabel}</Badge>
          <nav className="views" aria-label="视图切换">
            <button
              id="tab-current"
              type="button"
              className={`btn btn-ghost btn-sm${view === 'current' ? ' is-active' : ''}`}
              aria-current={view === 'current' ? 'page' : undefined}
              onClick={() => setView('current')}
            >
              当前运行
            </button>
            <button
              id="tab-history"
              type="button"
              className={`btn btn-ghost btn-sm${view !== 'current' ? ' is-active' : ''}`}
              aria-current={view !== 'current' ? 'page' : undefined}
              onClick={() => setView('history')}
            >
              本机历史
            </button>
          </nav>
        </div>
      </header>

      <div className="banner-host">
        {connection === 'lost' ? (
          <Banner tone="warn">
            状态连接中断：执行器可能仍在运行。<strong>连接失败不等于测试失败</strong>，恢复后按 runId +
            sequence 去重补齐。
          </Banner>
        ) : null}
        {run && !run.simulated && run.phase === 'pendingField' ? (
          <Banner tone="warn">待现场处理：机器占用未释放，禁止新一轮。</Banner>
        ) : null}
      </div>

      {view === 'current' ? <CurrentRunView session={session} /> : null}
      {view === 'history' ? <HistoryView session={session} onOpen={openHistory} /> : null}
      {view === 'history-detail' ? (
        <HistoryDetailView session={session} runId={historyRunId} onBack={() => setView('history')} />
      ) : null}

      <footer className="foot">{rangeNoteText(range, sectionRange)}</footer>

      <Toasts items={session.toasts} />
    </>
  );
}
