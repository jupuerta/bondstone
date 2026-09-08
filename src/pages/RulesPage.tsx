import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { analyzeArchitecture, ApiError, getArchitectureByShareToken } from "../api/architectureClient";
import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { ReferenceBack } from "../components/layout/ReferenceBack";
import { readRules, type RuleReading } from "../domain/rulesCatalog";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";
import type { AnalysisResult, RuleState } from "../types/architecture";

const STATES: RuleState[] = ["Fired", "Passed", "NotApplicable"];

export function RulesPage() {
  const t = useT();
  usePageTitle(t("ui.rules.eyebrow"));
  const { token } = useParams<{ token?: string }>();
  const { search } = useLocation();
  const wanted = new URLSearchParams(search).get("state");
  const filter = token !== undefined ? STATES.find((state) => state === wanted) ?? null : null;
  const [analysis, setAnalysis] = useState<AnalysisResult | null>(null);
  const [name, setName] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    Promise.all([getArchitectureByShareToken(token), analyzeArchitecture(token)])
      .then(([architecture, result]) => {
        if (cancelled) return;
        setName(architecture.name);
        setAnalysis(result);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof ApiError ? t(err.key, err.args) : t("web.engine.error.title"));
      });
    return () => {
      cancelled = true;
    };
  }, [token, t]);

  useEffect(() => {
    const id = decodeURIComponent(window.location.hash.slice(1));
    if (id) document.getElementById(id)?.scrollIntoView({ block: "center" });
  }, []);

  const allRules = readRules(analysis);
  const rules = filter === null ? allRules : allRules.filter((rule) => rule.state === filter);
  const overArchitecture = token !== undefined;
  const cols = overArchitecture
    ? "lg:grid-cols-[220px_minmax(0,1fr)_150px_110px_190px]"
    : "lg:grid-cols-[220px_minmax(0,1fr)_160px_120px]";

  return (
    <PageShell>
      <PageHead eyebrow={t("ui.rules.eyebrow")} title={t("ui.rules.headline")} lead={t("ui.rules.lead")} />

      <section className="page flex flex-col gap-4 pt-6 md:pt-8">
        <p className="max-w-[880px] text-small text-ink-2">
          {overArchitecture ? (name ? t("ui.rules.overNamed", { name }) : t("ui.rules.over")) : t("ui.rules.noArchitecture")}
        </p>
        {error && <p className="font-mono text-meta text-hot">{error}</p>}
        {filter !== null && analysis !== null && (
          <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-small">
            <span className="font-semibold">{t(`ui.rules.state.${filter}.count`, { count: rules.length })}</span>
            <Link to={`/rules/${encodeURIComponent(token ?? "")}`} className="link">
              {t("ui.rules.filter.clear")}
            </Link>
          </p>
        )}

        <div className={`eyebrow hidden gap-5 py-2.5 lg:grid ${cols}`}>
          <span>{t("web.reference.rules.col.rule")}</span>
          <span>{t("web.reference.rules.col.detects")}</span>
          <span>{t("web.reference.rules.col.area")}</span>
          <span>{t("web.reference.rules.col.fix")}</span>
          {overArchitecture && <span>{t("web.reference.rules.col.state")}</span>}
        </div>
        <ul className="hairline-rows flex flex-col border-b border-line">
          {rules.map((rule) => (
            <li key={rule.code} id={rule.code} className={`flex scroll-mt-6 flex-col gap-1.5 py-4 lg:grid lg:items-start lg:gap-5 ${cols}`}>
              <span className="font-mono text-code font-semibold">{rule.code}</span>
              <span className="text-small text-ink-2">{t(`ui.rules.${rule.code}.detects`)}</span>
              <span className="font-mono text-meta text-ink-3 lg:font-sans lg:text-small lg:text-ink">
                {t(`ui.area.${rule.area}`)}
                {rule.fix && <span className="lg:hidden"> · {t("ui.rules.hasFix")}</span>}
              </span>
              <span className="hidden text-code lg:block">
                {rule.fix ? (
                  <span className="inline-flex items-center gap-1.5 text-accent">
                    <span aria-hidden className="size-2 rounded-full bg-accent" />
                    {t("ui.rules.hasFix")}
                  </span>
                ) : (
                  <span className="text-ink-3">—</span>
                )}
              </span>
              {overArchitecture && <RuleStateCell rule={rule} />}
            </li>
          ))}
        </ul>
        {filter !== null && analysis !== null && rules.length === 0 && <p className="text-small text-ink-2">{t("ui.rules.filter.empty")}</p>}
        <p className="max-w-[880px] pt-4 text-small text-ink-2">{t("ui.rules.whyNotAll")}</p>
      </section>

      <ReferenceBack />
    </PageShell>
  );
}

function RuleStateCell({ rule }: { rule: RuleReading }) {
  const t = useT();
  if (rule.state === null) return <span className="font-mono text-meta text-ink-3">—</span>;
  if (rule.state === "Fired") {
    return <span className="text-small font-semibold text-hot">{rule.findings > 0 ? t("ui.rules.state.Fired.count", { count: rule.findings }) : t("ui.rules.state.Fired")}</span>;
  }
  if (rule.state === "Passed") return <span className="text-small font-semibold text-accent">{t("ui.rules.state.Passed")}</span>;
  return (
    <span className="flex flex-col gap-0.5 text-small text-ink-3">
      {t("ui.rules.state.NotApplicable")}
      {rule.reason && <span className="text-meta">{t(rule.reason)}</span>}
    </span>
  );
}
