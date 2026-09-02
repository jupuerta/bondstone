import type { ReactNode } from "react";
import { searchWithScenario } from "../../domain/scenarioUrl";
import { useT } from "../../i18n";
import { Button } from "../ui/Button";
import { Skeleton } from "../ui/Skeleton";
import { Diagram } from "./Diagram";
import { FindingCard, pickFinding } from "./FindingCard";
import { NodeList } from "./NodeList";
import { useEngine, type EngineData, type EngineScenario } from "./useEngine";

interface EnginePanelProps {
  shareToken: string | null;
  scenario: EngineScenario | null;
  /** Algo para la esquina derecha de la barra del panel: el conmutador antes/después. */
  headerRight?: ReactNode;
  /** Si es verdad, el panel ofrece «Abrir esta arquitectura» hacia /a/{token} con el fallo en la URL. */
  offerOpen?: boolean;
  id: string;
}

/** El panel oscuro: el motor sobre una arquitectura real, con sus tres estados. */
export function EnginePanel({ shareToken, scenario, headerRight, offerOpen = false, id }: EnginePanelProps) {
  const t = useT();
  const { state, retry } = useEngine(shareToken, scenario);

  return (
    <section className="panel overflow-hidden rounded-panel border border-panel-line bg-panel text-panel-ink shadow-[0_40px_80px_-48px_rgba(16,16,18,0.6)]" aria-busy={state.status === "loading"}>
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-panel-line px-4 py-3 font-mono text-meta text-panel-ink-2 md:px-5">
        <span>
          {state.status === "ready" ? headline(state.data, t) : shareToken === null ? t("web.engine.notConfigured") : `${t("ui.loading")}`}
        </span>
        {headerRight}
      </header>

      {shareToken === null && <p className="px-5 py-8 text-small text-panel-ink-2">{t("web.engine.notConfigured")}</p>}

      {shareToken !== null && state.status === "loading" && (
        <div className="flex flex-col gap-3 p-5" aria-hidden>
          <Skeleton width="60%" height={18} dark />
          <Skeleton height={140} dark />
          <Skeleton width="45%" dark />
          <Skeleton width="70%" dark />
        </div>
      )}

      {shareToken !== null && state.status === "error" && (
        <div className="flex flex-col items-start gap-3 p-6">
          <p className="text-body font-semibold">{t("web.engine.error.title")}</p>
          <p className="text-small text-panel-ink-2">{t("web.engine.error.body")}</p>
          <p className="font-mono text-meta text-panel-ink-3">{state.message}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="panel" size="sm" onClick={retry}>
              {t("web.engine.retry")}
            </Button>
            {offerOpen && (
              <Button variant="panel" size="sm" to={`/a/${encodeURIComponent(shareToken)}`}>
                {t("web.home.demo.open")}
              </Button>
            )}
          </div>
        </div>
      )}

      {shareToken !== null && state.status === "ready" && <Ready data={state.data} offerOpen={offerOpen} id={id} />}
    </section>
  );
}

function headline(data: EngineData, t: ReturnType<typeof useT>): string {
  const faulted = data.faults.flatMap((fault) => ("id" in fault ? data.architecture.nodes.filter((node) => node.id === fault.id) : []));
  const parts = [data.architecture.name];
  for (const node of faulted) parts.push(t("web.engine.fault", { name: node.name }));
  parts.push(t("web.engine.readOnly"));
  return parts.join(" · ");
}

function Ready({ data, offerOpen, id }: { data: EngineData; offerOpen: boolean; id: string }) {
  const t = useT();
  const faultIds = new Set(data.faults.flatMap((fault) => ("id" in fault ? [fault.id] : [])));
  const states = data.simulation?.states ?? null;
  const finding = pickFinding(data.analysis.findings, faultIds);
  const metrics = data.simulation?.metrics ?? null;
  const openHref = `/a/${encodeURIComponent(data.architecture.shareToken)}${searchWithScenario("", data.faults)}`;

  return (
    <div className="grid gap-6 p-4 md:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)] md:p-6">
      <div className="flex flex-col gap-4">
        <div className="hidden md:block">
          <Diagram architecture={data.architecture} states={states} faultIds={faultIds} id={id} />
        </div>
        <NodeList architecture={data.architecture} states={states} faultIds={faultIds} className="md:sr-only" />
        <p className="font-mono text-label text-panel-ink-3">{t("web.engine.legend")}</p>
        <div className="flex flex-wrap items-center justify-between gap-3 font-mono text-code">
          <span className="flex flex-wrap gap-4">
            {metrics && (
              <>
                <span className="text-hot-panel">● {t("web.engine.lost", { value: metrics.requestsAffected })}</span>
                <span className="text-panel-ink-2">{t("web.engine.downCount", { down: metrics.componentsDown, faults: faultIds.size })}</span>
              </>
            )}
          </span>
          {offerOpen && (
            <Button variant="panel" size="sm" to={openHref}>
              {t("web.home.demo.open")}
            </Button>
          )}
        </div>
      </div>
      {finding ? (
        <FindingCard finding={finding} shareToken={data.architecture.shareToken} />
      ) : (
        <p className="text-small text-panel-ink-2">{t("web.engine.noFinding")}</p>
      )}
    </div>
  );
}
