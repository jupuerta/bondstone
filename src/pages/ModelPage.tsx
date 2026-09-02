import { useEffect, useState } from "react";
import { ApiError } from "../api/architectureClient";
import { getModel, type ModelReference, type QueueingCurve } from "../api/modelClient";
import { PageHead } from "../components/layout/PageHead";
import { PageShell } from "../components/layout/PageShell";
import { ReferenceBack } from "../components/layout/ReferenceBack";
import { Skeleton } from "../components/ui/Skeleton";
import { useT } from "../i18n";
import { usePageTitle } from "../hooks/usePageTitle";

/** Mismos puntos de ejemplo que la página actual, para que el texto diga lo mismo. */
const COLLAPSE = 1.725;
const EXAMPLE_CALL_MS = 200;
const EXAMPLE_TIMEOUT_MS = 5_000;

export function factorAt(curve: QueueingCurve, utilization: number): number | null {
  let nearest: number | null = null;
  let distance = Infinity;
  for (const point of curve.points) {
    const gap = Math.abs(point.utilization - utilization);
    if (gap < distance) {
      distance = gap;
      nearest = point.factor;
    }
  }
  return distance <= 0.0125 ? nearest : null;
}

export function ModelPage() {
  const t = useT();
  usePageTitle(t("ui.model.eyebrow"));
  const [model, setModel] = useState<ModelReference | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getModel()
      .then((loaded) => {
        if (!cancelled) setModel(loaded);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.model.loadFailed"));
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  return (
    <PageShell>
      <PageHead eyebrow={t("ui.model.eyebrow")} title={t("ui.model.headline")} lead={t("ui.model.lead")} />

      <div className="page flex flex-col gap-14 pt-8 md:gap-20 md:pt-12">
        {error && <p className="font-mono text-meta text-hot">{error}</p>}
        {model === null && error === null && (
          <div className="flex flex-col gap-3" aria-busy>
            <Skeleton width="40%" height={24} />
            <Skeleton height={160} />
            <p className="font-mono text-label text-ink-3">{t("ui.loading")}</p>
          </div>
        )}

        {model !== null && (
          <>
            <section className="flex flex-col gap-6">
              <SectionHead title={t("ui.model.capacity")} lead={t("ui.model.capacityLead")} />
              <div className="eyebrow hidden gap-5 py-2 md:grid md:grid-cols-[1.4fr_1fr_1fr_1fr_1fr]">
                <span>{t("ui.model.component")}</span>
                <span>{t("ui.model.perInstance")}</span>
                <span>{t("ui.model.processing")}</span>
                <span>{t("ui.model.cost")}</span>
                <span>{t("ui.model.perUnit")}</span>
              </div>
              <ul className="hairline-rows flex flex-col border-b border-line">
                {model.components.map((component) => (
                  <li key={component.type} className="grid grid-cols-2 gap-2 py-3.5 md:grid-cols-[1.4fr_1fr_1fr_1fr_1fr] md:gap-5">
                    <span className="col-span-2 text-body font-semibold md:col-span-1">{t(`ui.node.${component.type}`)}</span>
                    <Cell label={t("ui.model.perInstance")} value={t("ui.model.rps", { value: component.capacityPerInstance })} />
                    <Cell label={t("ui.model.processing")} value={t("ui.model.ms", { value: component.processingMs })} />
                    <Cell label={t("ui.model.cost")} value={t("ui.model.units", { value: component.costPerInstance })} />
                    <Cell label={t("ui.model.perUnit")} value={component.costPerInstance > 0 ? String(Math.round(component.capacityPerInstance / component.costPerInstance)) : "—"} />
                  </li>
                ))}
              </ul>
              <div className="grid gap-3 md:grid-cols-3">
                <Note>{t("ui.model.costNote")}</Note>
                <Note>{t("ui.model.capacityNote")}</Note>
                <Note>{t("ui.model.fixedNote")}</Note>
              </div>
            </section>

            <section className="flex flex-col gap-6">
              <SectionHead title={t("ui.model.network")} lead={t("ui.model.networkLead")} />
              <div className="grid gap-4 md:grid-cols-2 md:items-start">
                <div className="flex flex-col gap-3 rounded-card border border-line bg-surface p-5">
                  <span className="eyebrow">{t("ui.model.hop")}</span>
                  <ul className="hairline-rows flex flex-col">
                    {[...model.network]
                      .sort((a, b) => a.ms - b.ms)
                      .map((hop) => (
                        <li key={hop.protocol} className="flex items-center justify-between py-2.5">
                          <span className="font-mono text-code">{hop.protocol}</span>
                          <span className="num text-small">{t("ui.model.ms", { value: hop.ms })}</span>
                        </li>
                      ))}
                  </ul>
                  <p className="text-meta text-ink-3">{t("ui.model.hopNote")}</p>
                </div>
                <div className="flex flex-col gap-4">
                  <Fact label={t("ui.model.saturation")} value={t("ui.model.percentValue", { value: model.saturationWarning })} body={t("ui.model.saturationBody")} />
                  <Fact label={t("ui.model.unbounded")} value={t("ui.model.ms", { value: model.unboundedWaitMs })} body={t("ui.model.unboundedBody", { value: model.unboundedWaitMs })} />
                  <Fact label={t("ui.model.hitRatio")} value={t("ui.model.hitRatioValue", { cache: model.defaultHitRatio, cdn: model.defaultCdnHitRatio })} body={t("ui.model.hitRatioBody")} />
                </div>
              </div>
            </section>

            <section className="flex flex-col gap-6">
              <SectionHead title={t("ui.model.queueing")} lead={t("ui.model.queueingLead")} />
              <div className="grid gap-5 rounded-card border border-line bg-surface p-5 md:grid-cols-[minmax(0,360px)_minmax(0,1fr)] md:p-6">
                <pre className="overflow-x-auto font-mono text-code leading-[1.9] text-ink">{`ρ < 1   →   min( 1 / (1 − ρ) , ${model.queueing.cap} )\nρ ≥ 1   →   ${model.queueing.cap} × ρ`}</pre>
                <p className="text-small text-ink-2">{t("ui.model.rho")}</p>
              </div>
              {factorAt(model.queueing, COLLAPSE) !== null && (
                <p className="max-w-[880px] text-small text-ink-2">
                  {t("ui.model.queueingExample", {
                    utilization: COLLAPSE,
                    base: EXAMPLE_CALL_MS,
                    cost: EXAMPLE_CALL_MS * factorAt(model.queueing, COLLAPSE)!,
                    timeout: EXAMPLE_TIMEOUT_MS,
                  })}
                </p>
              )}
            </section>
          </>
        )}
      </div>

      <ReferenceBack />
    </PageShell>
  );
}

function Cell({ label, value }: { label: string; value: string }) {
  return (
    <span className="flex flex-col gap-0.5 md:block">
      <span className="eyebrow md:hidden">{label}</span>
      <span className="num text-small">{value}</span>
    </span>
  );
}

function SectionHead({ title, lead }: { title: string; lead: string }) {
  return (
    <div className="flex flex-col gap-3">
      <h2 className="text-[30px] tracking-[-0.02em] md:text-section">{title}</h2>
      <p className="max-w-[880px] text-body text-ink-2">{lead}</p>
    </div>
  );
}

function Fact({ label, value, body }: { label: string; value: string; body: string }) {
  return (
    <div className="flex flex-col gap-2 rounded-card border border-line bg-surface p-5">
      <div className="flex items-baseline justify-between gap-3">
        <span className="eyebrow">{label}</span>
        <span className="num text-body font-semibold">{value}</span>
      </div>
      <p className="text-meta text-ink-3">{body}</p>
    </div>
  );
}

function Note({ children }: { children: string }) {
  return <p className="rounded-card border border-line bg-surface px-4 py-3.5 text-small text-ink-2">{children}</p>;
}
