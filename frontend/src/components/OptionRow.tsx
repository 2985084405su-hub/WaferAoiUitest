interface OptionRowProps {
  id: string;
  title: string;
  description: string;
  checked: boolean;
  disabled?: boolean;
  onChange: () => void;
}

/** 选项行：标题 + 一句后果说明，勾选状态由父组件控制。 */
export function OptionRow({ id, title, description, checked, disabled, onChange }: OptionRowProps) {
  return (
    <label className="opt" htmlFor={id}>
      <input id={id} type="checkbox" checked={checked} disabled={disabled} onChange={onChange} />
      <span>
        <strong>{title}</strong>
        <em>{description}</em>
      </span>
    </label>
  );
}
