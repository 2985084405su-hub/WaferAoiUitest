import type { InputHTMLAttributes } from 'react';

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'value'> {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  required?: boolean;
  mono?: boolean;
  hint?: string;
}

export function TextField({
  id,
  label,
  value,
  onChange,
  error,
  required = false,
  mono = false,
  hint,
  ...rest
}: TextFieldProps) {
  return (
    <div className={`field${error ? ' is-invalid' : ''}`}>
      <label htmlFor={id}>
        {label}
        {required ? <span className="req">*</span> : null}
      </label>
      <input
        {...rest}
        id={id}
        className={mono ? 'mono' : undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-err` : undefined}
      />
      {hint ? <p className="hint">{hint}</p> : null}
      {error ? (
        <p className="err" id={`${id}-err`}>
          {error}
        </p>
      ) : null}
    </div>
  );
}
