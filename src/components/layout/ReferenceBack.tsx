import { useT } from "../../i18n";
import { Button } from "../ui/Button";
import { Callout } from "../ui/Callout";
import { FIRST_CHALLENGE } from "./SiteHeader";

const DEMO = import.meta.env.VITE_DEMO_BEFORE_TOKEN || null;

/** El bloque final de las páginas de referencia: la vuelta al producto. */
export function ReferenceBack() {
  const t = useT();
  return (
    <section className="page py-12 md:py-16">
      <Callout label={t("web.reference.back.label")} className="md:px-8 md:py-7">
        <h2 className="text-[26px] tracking-[-0.02em] text-ink md:text-[32px]">{t("web.reference.back.title")}</h2>
        <div className="flex flex-col gap-2.5 md:flex-row md:flex-wrap">
          {DEMO && (
            <Button size="md" href="/#demo">
              {t("web.reference.back.demo")}
            </Button>
          )}
          <Button variant="ghost" size="md" to={FIRST_CHALLENGE}>
            {t("web.reference.back.challenge")}
          </Button>
        </div>
      </Callout>
    </section>
  );
}
