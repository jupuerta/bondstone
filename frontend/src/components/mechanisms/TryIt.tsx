import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError, createArchitecture, updateArchitecture } from "../../api/architectureClient";
import { mechanismLabel, type MechanismId } from "../../domain/mechanisms";
import { SANDBOXES, sandboxToDto } from "../../domain/sandboxes";
import { rememberWork } from "../../domain/savedWork";
import { useT } from "../../i18n";
import { Button } from "../ui/Button";
import { InlineError } from "../ui/InlineError";

export function TryIt({ id }: { id: MechanismId }) {
  const t = useT();
  const navigate = useNavigate();
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const open = () => {
    setOpening(true);
    setError(null);

    const name = t("ui.mechanisms.sandboxName", { mechanism: t(mechanismLabel(id)) });
    const { nodes, connections } = sandboxToDto(SANDBOXES[id]);

    createArchitecture(name)
      .then(async (created) => {
        rememberWork({ editToken: created.editToken, shareToken: created.shareToken, name: created.name, challengeSlug: null });
        await updateArchitecture(created.editToken, { name: created.name, version: created.version, nodes, connections });
        navigate(`/e/${encodeURIComponent(created.editToken)}`);
      })
      .catch((err: unknown) => {
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.mechanisms.tryFailed"));
        setOpening(false);
      });
  };

  return (
    <div className="flex flex-col gap-2 border-t border-line pt-4">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <Button variant="ghost" size="sm" onClick={open} loading={opening} loadingLabel={t("ui.mechanisms.trying")}>
          {t("ui.mechanisms.try")}
        </Button>
        <span className="text-small text-ink-3">{t("ui.mechanisms.tryLead")}</span>
      </div>
      {error !== null && <InlineError>{error}</InlineError>}
    </div>
  );
}
