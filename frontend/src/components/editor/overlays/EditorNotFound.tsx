import { useT } from "../../../i18n";
import { Button } from "../../ui/Button";

export function EditorNotFound({ message, onRetry }: { message: string; onRetry: () => void }) {
  const t = useT();
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 bg-canvas p-6 text-center">
      <p className="text-body font-semibold text-panel-ink">{message}</p>
      <div className="flex flex-wrap justify-center gap-2">
        <Button variant="panel" size="sm" onClick={onRetry}>
          {t("ui.editor.retry")}
        </Button>
        <Button variant="panel" size="sm" to="/">
          {t("ui.editor.backHome")}
        </Button>
      </div>
    </div>
  );
}
