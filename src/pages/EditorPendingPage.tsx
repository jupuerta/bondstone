import { useParams } from "react-router-dom";
import { PageShell } from "../components/layout/PageShell";
import { Button } from "../components/ui/Button";
import { Callout } from "../components/ui/Callout";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

const LEGACY = (import.meta.env.VITE_LEGACY_APP_URL ?? "http://localhost:8080").replace(/\/$/, "");

/** /e/:token — el editor no existe todavía en este frontend; se abre en el actual. */
export function EditorPendingPage() {
  const t = useT();
  const { token = "" } = useParams<{ token: string }>();
  usePageTitle(t("web.editor.pending.title"));

  return (
    <PageShell>
      <section className="page flex max-w-[760px] flex-col gap-6 py-16 md:py-24">
        <h1 className="text-[36px] leading-none tracking-[-0.03em] md:text-[48px]">{t("web.editor.pending.title")}</h1>
        <Callout label="ArchForge">
          <p className="text-body">{t("web.editor.pending.body")}</p>
          <Button size="md" href={`${LEGACY}/e/${encodeURIComponent(token)}`} className="self-start">
            {t("web.editor.pending.open")}
          </Button>
        </Callout>
      </section>
    </PageShell>
  );
}
