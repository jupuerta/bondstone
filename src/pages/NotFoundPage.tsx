import { PageShell } from "../components/layout/PageShell";
import { Button } from "../components/ui/Button";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

export function NotFoundPage() {
  const t = useT();
  usePageTitle("404");

  return (
    <PageShell>
      <section className="page flex flex-col gap-5 py-20 md:items-center md:text-center md:py-36">
        <span className="font-mono text-code tracking-[0.12em] text-ink-3">404</span>
        <h1 className="text-[38px] leading-none tracking-[-0.03em] md:text-[56px]">{t("web.notFound.text")}</h1>
        <div className="flex flex-col gap-3 md:flex-row">
          <Button size="md" to="/">
            {t("web.notFound.home")}
          </Button>
          <Button variant="ghost" size="md" to="/challenges">
            {t("web.notFound.challenges")}
          </Button>
        </div>
      </section>
    </PageShell>
  );
}
