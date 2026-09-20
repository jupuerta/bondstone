import { useState } from "react";
import { ApiError, deleteArchitecture } from "../../../api/architectureClient";
import { useEscape } from "../../../hooks/useEscape";
import { useT } from "../../../i18n";
import { Button } from "../../ui/Button";

interface DeleteDialogProps {
  editToken: string;
  name: string;
  onDeleted: () => void;
  onClose: () => void;
}

/**
 * La confirmación de lo único que no se puede deshacer. Lo que la distingue del resto de diálogos:
 *
 *  - El foco empieza en «Conservarla» (`autoFocus`). En un diálogo que borra, el botón que se
 *    pulsa sin leer tiene que ser el que no hace nada.
 *  - Escape cancela, y por `useEscape`: un `useEffect` que dependa de `onClose` se vuelve a montar
 *    en cada dibujo y se come la pulsación.
 *  - Mientras borra, los dos botones se desactivan. Borrar dos veces devolvería un 404 que el
 *    cliente ya trata como éxito, pero un diálogo que acepta clics mientras trabaja se siente roto.
 */
export function DeleteDialog({ editToken, name, onDeleted, onClose }: DeleteDialogProps) {
  const t = useT();
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEscape(onClose);

  const remove = () => {
    setWorking(true);
    setError(null);
    deleteArchitecture(editToken)
      .then(onDeleted)
      .catch((err: unknown) => {
        setError(err instanceof ApiError ? t(err.key, err.args) : t("editor.delete.failed"));
        setWorking(false);
      });
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="editor-delete-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-canvas/80 p-6"
      onClick={working ? undefined : onClose}
    >
      <div
        className="flex w-full max-w-[440px] flex-col gap-3 rounded-panel border border-panel-line-2 bg-panel-2 p-5 text-panel-ink"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="editor-delete-title" className="font-display text-panel font-semibold text-panel-ink">
          {t("editor.delete.title")}
        </h2>
        <p className="text-small text-panel-ink-2">{t("editor.delete.lead", { name })}</p>
        <p className="text-small font-semibold text-hot-panel">{t("editor.delete.warning")}</p>
        {error && <p className="text-small text-hot-panel">{error}</p>}
        <div className="mt-2 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button autoFocus variant="panel" size="sm" onClick={onClose} disabled={working}>
            {t("editor.delete.cancel")}
          </Button>
          <Button variant="panelDanger" size="sm" onClick={remove} disabled={working} loading={working} loadingLabel={t("editor.delete.working")}>
            {t("editor.delete.confirm")}
          </Button>
        </div>
      </div>
    </div>
  );
}
