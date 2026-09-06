import type { SaveStatus } from "../../../hooks/useAutosave";
import { useT } from "../../../i18n";
import { SpinnerIcon } from "../../ui/Icons";

interface SaveStateProps {
  status: SaveStatus;
  version: number;
  error: string | null;
  onRetry: () => void;
  narrow: boolean;
}

const LABEL: Record<SaveStatus, string> = {
  idle: "editor.save.clean",
  pending: "editor.save.pending",
  saving: "ui.save.saving",
  saved: "ui.save.saved",
  error: "ui.save.failed",
  conflict: "ui.save.conflict",
};

export function SaveState({ status, version, error, onRetry, narrow }: SaveStateProps) {
  const t = useT();
  const bad = status === "error" || status === "conflict";
  const tone = bad ? "text-hot-panel" : status === "pending" ? "text-warm-panel" : status === "saved" ? "text-ok-panel" : "text-panel-ink-2";

  return (
    <div role="status" aria-live="polite" className={`flex min-h-10 shrink-0 items-center gap-2 font-mono text-meta ${tone}`}>
      <Glyph status={status} />
      <span className="whitespace-nowrap" title={error ?? undefined}>
        {t(LABEL[status])}
        {!narrow && !bad && <span className="text-panel-ink-3"> · v{version}</span>}
      </span>
      {status === "error" && (
        <button type="button" onClick={onRetry} className="inline-flex min-h-8 items-center rounded-pill border border-hot-panel/60 px-2.5 text-meta text-hot-panel hover:bg-hot-panel/10">
          {t("ui.editor.retry")}
        </button>
      )}
    </div>
  );
}

function Glyph({ status }: { status: SaveStatus }) {
  if (status === "saving") return <SpinnerIcon size={12} />;
  const common = { width: 12, height: 12, viewBox: "0 0 12 12", "aria-hidden": true, fill: "none", stroke: "currentColor", strokeWidth: 1.6, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };
  if (status === "saved") return <svg {...common}><path d="M2.5 6.5l2.5 2.5 4.5-5" /></svg>;
  if (status === "pending") return <svg {...common}><circle cx="6" cy="6" r="4" strokeDasharray="2 2" /></svg>;
  if (status === "error") return <svg {...common}><path d="M6 2.5v4M6 9v.5" /><circle cx="6" cy="6" r="4.5" /></svg>;
  if (status === "conflict") return <svg {...common}><path d="M2 3h8M2 9h8M4 6h4" /></svg>;
  return <svg {...common}><circle cx="6" cy="6" r="1.6" fill="currentColor" stroke="none" /></svg>;
}
