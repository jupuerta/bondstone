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
  tap?: boolean;
}

/** Filtro exclusivo: una opción activa, en negro (o en blanco sobre el panel). */
export function Segmented<T extends string>({ options, value, onChange, label, dark = false, tap = false }: SegmentedProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={`inline-flex rounded-pill border p-[3px] font-mono text-code ${dark ? "border-panel-line" : "border-line-2"} ${tap ? "w-full" : ""}`}
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
            className={`rounded-pill px-3.5 transition-colors ${tap ? "min-h-[38px] flex-1" : "min-h-8"} ${
              on
                ? dark
                  ? "bg-panel-ink font-semibold text-panel-on-ink"
                  : "bg-ink font-semibold text-white"
                : dark
                  ? "text-panel-ink-2 hover:bg-wash/8"
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
