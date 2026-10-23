import { useEffect } from 'react';
import { Banner } from '../components/Banner';
import { HistoryTable } from '../components/HistoryTable';
import type { Session } from '../state/useSession';

export function HistoryView({ session, onOpen }: { session: Session; onOpen: (runId: string) => void }) {
  useEffect(() => {
    void session.refreshHistory();
    // 只在进入视图时刷新一次；后续由用户手动返回触发。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <main className="view">
      <section className="card">
        <div className="card-head">
          <h2>本机历史运行</h2>
          <span className="muted">只读 · 按 runId 关联证据</span>
        </div>
        <Banner tone="info">
          历史记录只读：不重新执行步骤、不重复提交确认命令、不影响当前运行。
        </Banner>
        <HistoryTable runs={session.history} onOpen={onOpen} />
      </section>
    </main>
  );
}
