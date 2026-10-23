import type { ToastMessage } from '../state/useSession';

/** Toast 只承载“已发生”的短消息，不承载唯一关键信息。 */
export function Toasts({ items }: { items: ToastMessage[] }) {
  return (
    <div className="toast-host" role="status" aria-live="polite">
      {items.map((item) => (
        <div key={item.id} className={`toast${item.tone === 'error' ? ' is-error' : ''}`}>
          {item.text}
        </div>
      ))}
    </div>
  );
}
