import { useCallback, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError, createArchitecture } from "../api/architectureClient";
import { editorPath, rememberWork } from "../domain/savedWork";
import { useT } from "../i18n";

export interface StartFromScratch {
  start: () => void;
  creating: boolean;
  error: string | null;
}

export function useStartFromScratch(): StartFromScratch {
  const t = useT();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const start = useCallback(() => {
    setCreating(true);
    setError(null);
    createArchitecture(t("ui.home.untitled"))
      .then((architecture) => {
        rememberWork({ editToken: architecture.editToken, shareToken: architecture.shareToken, name: architecture.name, challengeSlug: null });
        navigate(editorPath({ editToken: architecture.editToken, challengeSlug: null }));
      })
      .catch((err: unknown) => {
        setCreating(false);
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.home.createFailed"));
      });
  }, [navigate, t]);

  return { start, creating, error };
}
