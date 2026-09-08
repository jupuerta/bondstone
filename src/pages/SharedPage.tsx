import { useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { ApiError, forkArchitecture } from "../api/architectureClient";
import { ReadOnlyEditor } from "../components/editor/ReadOnlyEditor";
import { Button } from "../components/ui/Button";
import { CloseIcon, LogoMark } from "../components/ui/Icons";
import { InlineError } from "../components/ui/InlineError";
import { challengeOf, rememberWork } from "../domain/savedWork";
import { useT } from "../i18n";

const DISMISSED_KEY = "archforge.web.stripDismissed";

function readDismissed(): boolean {
  try {
    return window.localStorage.getItem(DISMISSED_KEY) === "1";
  } catch {
    return false;
  }
}

export function SharedPage() {
  const t = useT();
  const navigate = useNavigate();
  const { token = "" } = useParams<{ token: string }>();
  const { search } = useLocation();
  const [dismissed, setDismissed] = useState(readDismissed);
  const [copying, setCopying] = useState(false);
  const [copyError, setCopyError] = useState<string | null>(null);

  const dismiss = () => {
    setDismissed(true);
    try {
      window.localStorage.setItem(DISMISSED_KEY, "1");
    } catch {
      return;
    }
  };

  const openCopy = () => {
    setCopying(true);
    setCopyError(null);
    forkArchitecture(token)
      .then((fork) => {
        rememberWork({ editToken: fork.editToken, shareToken: fork.shareToken, name: fork.name, challengeSlug: challengeOf(search) });
        navigate(`/e/${encodeURIComponent(fork.editToken)}${search}`);
      })
      .catch((err: unknown) => {
        setCopying(false);
        setCopyError(err instanceof ApiError ? t(err.key, err.args) : t("ui.editor.openCopyFailed"));
      });
  };

  return (
    <div className="flex h-dvh flex-col bg-bg text-ink">
      {dismissed ? (
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-b border-line bg-surface-2 px-4 py-2.5 text-small md:px-6">
          <span className="text-ink-2">{t("ui.editor.readOnlyNotice")}</span>
          <Button size="sm" onClick={openCopy} loading={copying} loadingLabel={t("web.shared.copying")}>
            {t("web.shared.copy")}
          </Button>
        </div>
      ) : (
        <aside className="shrink-0 border-b border-accent-line bg-accent-tint" aria-label="ArchForge">
          <div className="flex flex-col gap-3 px-4 py-3.5 md:flex-row md:items-center md:gap-5 md:px-6">
            <div className="flex items-start gap-3 md:flex-1 md:items-center">
              <LogoMark size={22} className="mt-0.5 shrink-0 text-accent md:mt-0" />
              <p className="flex-1 text-small leading-normal md:text-[15px]">{t("web.shared.first")}</p>
              <button type="button" onClick={dismiss} aria-label={t("web.shared.dismiss")} className="-mt-2 -mr-2 inline-flex size-11 shrink-0 items-center justify-center rounded-pill text-ink-2 md:hidden">
                <CloseIcon size={20} />
              </button>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button size="sm" onClick={openCopy} loading={copying} loadingLabel={t("web.shared.copying")} className="flex-1 md:flex-none">
                {t("web.shared.copy")}
              </Button>
              <Button variant="ghost" size="sm" to="/challenges" className="flex-1 md:flex-none">
                {t("web.shared.challenges")}
              </Button>
              <Link to="/" className="link text-small md:ml-1">
                {t("web.shared.whatIsThis")}
              </Link>
              <button type="button" onClick={dismiss} aria-label={t("web.shared.dismiss")} className="hidden size-11 items-center justify-center rounded-pill text-ink-2 md:inline-flex">
                <CloseIcon size={20} />
              </button>
            </div>
          </div>
          {copyError && (
            <div className="px-4 pb-3 md:px-6">
              <InlineError>{copyError}</InlineError>
            </div>
          )}
        </aside>
      )}

      <main id="main" className="panel flex min-h-0 flex-1 flex-col bg-panel text-panel-ink">
        <ReadOnlyEditor shareToken={token} search={search} onOpenCopy={openCopy} copying={copying} copyError={dismissed ? copyError : null} />
      </main>
    </div>
  );
}
