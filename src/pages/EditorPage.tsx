import { useEffect, useState } from "react";
import { useLocation, useParams } from "react-router-dom";
import { ApiError, getArchitectureByEditToken, NotFoundError } from "../api/architectureClient";
import { Editor } from "../components/editor/Editor";
import { EditorLoading } from "../components/editor/overlays/EditorLoading";
import { EditorNotFound } from "../components/editor/overlays/EditorNotFound";
import { forgetWork } from "../domain/savedWork";
import { useT } from "../i18n";
import type { ArchitectureDto } from "../types/architecture";

type PageState = { status: "loading" } | { status: "error"; message: string } | { status: "ready"; architecture: ArchitectureDto };

export function EditorPage() {
  const t = useT();
  const { token = "" } = useParams<{ token: string }>();
  const { search } = useLocation();
  const [state, setState] = useState<PageState>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState({ status: "loading" });
    getArchitectureByEditToken(token)
      .then((architecture) => {
        if (!cancelled) setState({ status: "ready", architecture });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (err instanceof NotFoundError) {
          forgetWork(token);
          setState({ status: "error", message: t("ui.editor.notFound") });
        } else {
          setState({ status: "error", message: err instanceof ApiError ? t(err.key, err.args) : t("ui.editor.loadFailed") });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [token, attempt, t]);

  return (
    <main id="main" className="panel flex h-dvh flex-col bg-panel text-panel-ink">
      {state.status === "loading" && <EditorLoading />}
      {state.status === "error" && <EditorNotFound message={state.message} onRetry={() => setAttempt((n) => n + 1)} />}
      {state.status === "ready" && <Editor key={`${token}:${attempt}`} architecture={state.architecture} editToken={token} search={search} onReload={() => setAttempt((n) => n + 1)} />}
    </main>
  );
}
