import type { CSSProperties, ReactNode, Ref } from "react";

interface HelpSheetProps {
  ref?: Ref<HTMLDivElement>;
  id?: string;
  header: ReactNode;
  children: ReactNode;
  style: CSSProperties;
  anchored: "absolute" | "fixed";
  theme?: string;
  onMouseEnter: () => void;
  onMouseLeave: () => void;
}

export function HelpSheet({ ref, id, header, children, style, anchored, theme, onMouseEnter, onMouseLeave }: HelpSheetProps) {
  return (
    <div ref={ref} id={id} role="tooltip" data-theme={theme} onMouseEnter={onMouseEnter} onMouseLeave={onMouseLeave} style={style} className={`panel ${anchored} z-40 overflow-y-auto rounded-panel border border-panel-line-2 bg-panel-2 font-sans text-[14px] normal-case tracking-normal text-panel-ink shadow-[0_30px_60px_-30px_rgba(0,0,0,.8)]`}>
      <div className="flex items-center gap-2.5 border-b border-panel-line px-3 py-2.5">{header}</div>
      <div className="px-3.5 py-3">{children}</div>
    </div>
  );
}
