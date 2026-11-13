import { Link } from "react-router-dom";
import { useT } from "../../i18n";
import type { RuleFinding, Severity } from "../../types/architecture";
import { Chip, type ChipVariant } from "../ui/Chip";
import { ArrowIcon } from "../ui/Icons";

const SEVERITY: Record<Severity, { variant: ChipVariant; key: string }> = {
  Critical: { variant: "critical", key: "web.engine.severity.Critical" },
  Warning: { variant: "warning", key: "web.engine.severity.Warning" },
  Info: { variant: "info", key: "web.engine.severity.Info" },
  Good: { variant: "info", key: "web.engine.severity.Good" },
};

/** Elige qué hallazgo enseñar: el más grave, y entre iguales el que hable del componente tumbado. */
export function pickFinding(findings: RuleFinding[], faultIds: Set<string>): RuleFinding | null {
  const rank: Record<Severity, number> = { Critical: 0, Warning: 1, Info: 2, Good: 3 };
  const candidates = findings.filter((finding) => finding.severity !== "Good");
  // Entre dos de la misma gravedad, primero el más concreto (el que señala uno o dos elementos, no el
  // sistema entero) y, entre esos, el que habla del componente tumbado. Sin este orden, «el sistema
  // está ciego» ganaría siempre: nombra a todos los componentes, incluido el que se cae.
  const specificity = (f: RuleFinding) => (f.elements.length === 0 ? 3 : f.elements.length <= 2 ? 0 : f.elements.length <= 4 ? 1 : 2);
  const aboutFault = (f: RuleFinding) => (f.elements.some((element) => faultIds.has(element.id)) ? 0 : 1);
  candidates.sort((a, b) => rank[a.severity] - rank[b.severity] || specificity(a) - specificity(b) || aboutFault(a) - aboutFault(b));
  return candidates[0] ?? null;
}

export function FindingCard({ finding, shareToken }: { finding: RuleFinding; shareToken: string }) {
  const t = useT();
  const severity = SEVERITY[finding.severity];

  return (
    <article className="flex flex-col gap-3 rounded-card border border-panel-line bg-panel-2 p-5">
      <span className="eyebrow text-panel-ink-3">{t("web.home.demo.findingLabel")}</span>
      <div className="flex flex-wrap items-center gap-2">
        <Chip variant={severity.variant}>{t(severity.key)}</Chip>
        <Chip variant="panel">{t(`ui.area.${finding.area}`)}</Chip>
        <Chip variant="code">{finding.code}</Chip>
      </div>
      <p className="text-body font-semibold leading-snug text-panel-ink">{t(finding.message)}</p>
      <p className="text-small text-panel-ink-2">{t(finding.explanation)}</p>
      {finding.suggestion && (
        <p className="text-small text-panel-ink-2">
          <span className="font-semibold text-panel-ink">{t("web.engine.whatToDo")}</span> {t(finding.suggestion)}
        </p>
      )}
      <Link to={`/rules/${encodeURIComponent(shareToken)}#${finding.code}`} className="inline-flex items-center gap-1.5 text-small font-semibold text-panel-ink no-underline hover:underline">
        {t("web.home.demo.seeRule")}
        <ArrowIcon size={16} />
      </Link>
    </article>
  );
}
