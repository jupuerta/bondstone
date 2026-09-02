interface Option<T extends string> {
  value: T;
  label: string;
}

interface SegmentedProps<T extends string> {
  options: Option<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
  dark?: boolean;
}

/** Filtro exclusivo: una opción activa, en negro (o en blanco sobre el panel). */
export function Segmented<T extends string>({ options, value, onChange, label, dark = false }: SegmentedProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={`inline-flex rounded-pill border p-[3px] font-mono text-code ${dark ? "border-panel-line" : "border-line-2"}`}
    >
      {options.map((option) => {
        const on = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={on}
            onClick={() => onChange(option.value)}
            className={`min-h-8 rounded-pill px-3.5 transition-colors ${
              on
                ? dark
                  ? "bg-panel-ink font-semibold text-panel"
                  : "bg-ink font-semibold text-white"
                : dark
                  ? "text-panel-ink-2 hover:bg-white/8"
                  : "text-ink-2 hover:bg-[color-mix(in_oklab,var(--af-ink)_6%,transparent)]"
            }`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
