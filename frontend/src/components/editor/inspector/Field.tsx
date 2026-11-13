import { useId, type ReactNode } from "react";

interface FieldProps {
  label: ReactNode;
  children: ReactNode;
  disabled?: boolean;
}

const BOX = "flex min-h-[30px] w-full items-center rounded-node border px-2.5 text-[13px]";
const LIVE = "border-panel-line bg-panel-2 text-panel-ink hover:border-panel-line-2";
const OFF = "border-dashed border-panel-line text-panel-ink-3";

export function Field({ label, children, disabled = false }: FieldProps) {
  return (
    <div className="flex flex-col gap-1">
      <span className="eyebrow text-panel-ink-3">{label}</span>
      <div className={`${BOX} ${disabled ? OFF : LIVE}`}>{children}</div>
    </div>
  );
}

interface TextFieldProps {
  label: ReactNode;
  value: string | number;
  kind?: "text" | "number";
  min?: number;
  max?: number;
  step?: number;
  maxLength?: number;
  onChange: (raw: string) => void;
  onBlur?: () => void;
}

export function TextField({ label, value, kind = "text", min, max, step, maxLength, onChange, onBlur }: TextFieldProps) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="eyebrow text-panel-ink-3">
        {label}
      </label>
      <input
        id={id}
        type={kind}
        inputMode={kind === "number" ? "decimal" : undefined}
        min={min}
        max={max}
        step={step}
        maxLength={maxLength}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onBlur={onBlur}
        className={`${BOX} ${LIVE} ${kind === "number" ? "num" : ""} outline-none focus-visible:border-accent-panel`}
      />
    </div>
  );
}

interface CheckFieldProps {
  label: ReactNode;
  hint?: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}

export function CheckField({ label, hint, checked, onChange }: CheckFieldProps) {
  const id = useId();
  return (
    <div className="flex items-start gap-2.5 py-0.5">
      <input id={id} type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} className="mt-[3px] size-4 shrink-0 accent-[var(--af-accent-panel)]" />
      <label htmlFor={id} className="flex min-w-0 flex-col gap-0.5 text-[13px] text-panel-ink">
        {label}
        {hint && <span className="text-meta text-panel-ink-3">{hint}</span>}
      </label>
    </div>
  );
}

interface ChoiceOption<T> {
  value: T;
  label: string;
}

interface ChoiceFieldProps<T> {
  label: ReactNode;
  options: ChoiceOption<T>[];
  value: T;
  onChange: (value: T) => void;
}

export function ChoiceField<T extends string | boolean | undefined>({ label, options, value, onChange }: ChoiceFieldProps<T>) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <span id={id} className="eyebrow text-panel-ink-3">{label}</span>
      <div role="radiogroup" aria-labelledby={id} className="flex flex-wrap gap-1.5">
        {options.map((option) => {
          const on = option.value === value;
          return (
            <button
              key={String(option.value)}
              type="button"
              role="radio"
              aria-checked={on}
              onClick={() => onChange(option.value)}
              className={`inline-flex min-h-[26px] items-center rounded-pill border px-2.5 font-mono text-label uppercase tracking-[0.06em] transition-colors ${
                on ? "border-panel-ink bg-panel-ink font-semibold text-panel-on-ink" : "border-panel-line text-panel-ink-2 hover:border-panel-line-2 hover:text-panel-ink"
              }`}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

interface SelectFieldProps<T extends string> {
  label: ReactNode;
  hint?: string;
  options: ChoiceOption<T>[];
  value: T;
  onChange: (value: T) => void;
}

export function SelectField<T extends string>({ label, hint, options, value, onChange }: SelectFieldProps<T>) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="eyebrow text-panel-ink-3">
        {label}
      </label>
      <select id={id} value={value} onChange={(event) => onChange(event.target.value as T)} className={`${BOX} ${LIVE} appearance-none outline-none focus-visible:border-accent-panel`}>
        {options.map((option) => (
          <option key={option.value} value={option.value} className="bg-panel-2 text-panel-ink">
            {option.label}
          </option>
        ))}
      </select>
      {hint && <span className="text-meta text-panel-ink-3">{hint}</span>}
    </div>
  );
}
