import { useEffect, useMemo, useRef, useState } from "react";
import { ApiError, analyzeArchitecture, getArchitectureByShareToken } from "../../../api/architectureClient";
import { openChallengeSolution, previewChallengeSolution } from "../../../api/challengeClient";
import { useT } from "../../../i18n";
import { useEscape } from "../../../hooks/useEscape";
import type { AnalysisResult } from "../../../types/architecture";
import { layoutOf, type DiagramLayout } from "../../engine/layout";
import { Button } from "../../ui/Button";
import { Skeleton } from "../../ui/Skeleton";
import { compareGraphs, type ComparableGraph } from "../challenge/graphDiff";
import { Thumbnail } from "../starters/Thumbnail";

interface SolutionCompareProps {
  slug: string;
  referenceName: string;
  shareToken: string;
  onClose: () => void;
}

interface Side {
  name: string;
  graph: ComparableGraph;
  analysis: AnalysisResult;
}

type State = { status: "loading" } | { status: "error"; message: string } | { status: "ready"; yours: Side; reference: Side };

export function SolutionCompare({ slug, referenceName, shareToken, onClose }: SolutionCompareProps) {
  const t = useT();
  const box = useRef<HTMLDivElement>(null);
  const [state, setState] = useState<State>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);
  const [opening, setOpening] = useState(false);

  useEffect(() => {
    box.current?.querySelector<HTMLElement>("button")?.focus();
  }, []);

  useEscape(onClose);

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: "loading" });
    const mine = Promise.all([getArchitectureByShareToken(shareToken), analyzeArchitecture(shareToken, { signal: controller.signal })]).then(([architecture, analysis]) => ({
      name: architecture.name,
      graph: architecture,
      analysis,
    }));
    const theirs = previewChallengeSolution(slug, { signal: controller.signal }).then((preview) => ({ name: preview.name, graph: preview, analysis: preview.analysis }));
    Promise.all([mine, theirs])
      .then(([yours, reference]) => {
        if (!controller.signal.aborted) setState({ status: "ready", yours, reference });
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setState({ status: "error", message: err instanceof ApiError ? t(err.key, err.args) : t("ui.challenge.solutionFailed") });
      });
    return () => controller.abort();
  }, [slug, shareToken, attempt, t]);

  const openReference = () => {
    const tab = window.open("", "_blank");
    setOpening(true);
    openChallengeSolution(slug, referenceName)
      .then((copy) => {
        if (!tab) return;
        tab.opener = null;
        tab.location.href = `/a/${encodeURIComponent(copy.shareToken)}`;
      })
      .catch(() => tab?.close())
      .finally(() => setOpening(false));
  };

  return (
    <div role="dialog" aria-modal="true" aria-labelledby="editor-compare-title" className="fixed inset-0 z-50 flex items-center justify-center bg-canvas/80 p-4 md:p-6" onClick={onClose}>
      <div ref={box} className="flex max-h-full w-full max-w-[880px] flex-col gap-3 overflow-y-auto rounded-panel border border-panel-line-2 bg-panel-2 p-3.5 text-panel-ink" onClick={(event) => event.stopPropagation()}>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 id="editor-compare-title" className="font-display text-[15px] leading-[1.2] font-semibold text-panel-ink">
              {t("ui.compare.title")}
            </h2>
            <p className="mt-1 text-[12px] leading-[1.45] text-panel-ink-2">{t("ui.compare.lead")}</p>
          </div>
          <button type="button" onClick={onClose} aria-label={t("ui.close")} className="-mt-1 -mr-1 inline-flex size-9 shrink-0 items-center justify-center rounded-pill text-panel-ink-3 hover:text-panel-ink">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden>
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </div>

        {state.status === "loading" && (
          <div className="flex flex-col gap-3" aria-busy>
            <div className="grid gap-3.5 md:grid-cols-2">
              <Skeleton height={180} dark />
              <Skeleton height={180} dark />
            </div>
            <p className="text-[12px] leading-[1.45] text-panel-ink-2">{t("ui.compare.loading")}</p>
          </div>
        )}
        {state.status === "error" && (
          <div className="flex flex-col items-start gap-2">
            <p className="text-[12px] leading-[1.45] text-hot-panel">{state.message}</p>
            <Button variant="panel" size="sm" onClick={() => setAttempt((n) => n + 1)}>
              {t("web.engine.retry")}
            </Button>
          </div>
        )}
        {state.status === "ready" && <Comparison yours={state.yours} reference={state.reference} />}

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-panel-line pt-3">
          <span className="text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.compare.savedNote")}</span>
          <div className="flex gap-2">
            <Button variant="panel" size="sm" onClick={openReference} disabled={state.status !== "ready" || opening} loading={opening} loadingLabel={t("ui.compare.opening")}>
              {t("ui.challenge.seeSolution")}
            </Button>
            <Button variant="panelPrimary" size="sm" onClick={onClose}>
              {t("ui.close")}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}

function Comparison({ yours, reference }: { yours: Side; reference: Side }) {
  const t = useT();
  const [mineFrame, theirsFrame] = useMemo(() => matchedFrames(yours.graph, reference.graph), [yours.graph, reference.graph]);
  const differences = useMemo(() => compareGraphs(yours.graph, reference.graph), [yours.graph, reference.graph]);

  return (
    <>
      <div className="grid gap-3.5 md:grid-cols-2">
        <Pane label={t("ui.compare.yours")} side={yours} frame={mineFrame} />
        <Pane label={t("ui.compare.reference")} side={reference} frame={theirsFrame} />
      </div>
      <div className="border-t border-panel-line pt-3">
        <p className="eyebrow flex items-center gap-2 text-panel-ink-3">
          {t("ui.compare.differences")}
          {differences.length > 0 && <span className="num rounded-pill border border-panel-line px-1.5 text-[11px] text-panel-ink-2">{differences.length}</span>}
        </p>
        {differences.length === 0 ? (
          <p className="mt-2 text-[12px] leading-[1.45] text-panel-ink-2">{t("ui.compare.noDifferences")}</p>
        ) : (
          <>
            <ul className="mt-2 flex flex-col gap-1">
              {differences.map((difference, index) => (
                <li key={index} className="flex gap-2 text-[12px] leading-[1.45] text-panel-ink-2">
                  <span aria-hidden className="mt-[7px] size-1 shrink-0 rounded-full bg-panel-line-2" />
                  <span>{t(difference.text)}</span>
                </li>
              ))}
            </ul>
            <p className="mt-2.5 text-[11px] leading-[1.45] text-panel-ink-3">{t("ui.compare.differencesLead")}</p>
          </>
        )}
      </div>
    </>
  );
}

function Pane({ label, side, frame }: { label: string; side: Side; frame: DiagramLayout }) {
  const t = useT();
  const critical = side.analysis.summary.critical;

  return (
    <div className="flex min-w-0 flex-col gap-[5px]">
      <span className="eyebrow text-panel-ink-3">{label}</span>
      <Thumbnail nodes={side.graph.nodes} connections={side.graph.connections} frame={frame} />
      <div className="mt-1 flex items-center gap-2.5">
        <span className="num text-[22px] leading-none font-semibold text-panel-ink">{side.analysis.score ?? "—"}</span>
        <span className="min-w-0 flex-1 truncate text-[12px] font-medium text-panel-ink">{side.name}</span>
        {critical > 0 && <span className="num shrink-0 text-[11px] text-hot-panel">{t("ui.starter.criticals", { count: critical })}</span>}
      </div>
    </div>
  );
}

function matchedFrames(a: ComparableGraph, b: ComparableGraph): [DiagramLayout, DiagramLayout] {
  const own = [layoutOf(a), layoutOf(b)];
  const width = Math.max(...own.map((layout) => layout.width));
  const height = Math.max(...own.map((layout) => layout.height));
  const fontScale = Math.max(1, width / 760);
  const framed = own.map((layout): DiagramLayout => {
    const [minX, minY] = layout.viewBox.split(" ").map(Number);
    const x = minX - (width - layout.width) / 2;
    const y = minY - (height - layout.height) / 2;
    return { viewBox: `${x} ${y} ${width} ${height}`, width, height, boxes: layout.boxes, fontScale };
  });
  return [framed[0], framed[1]];
}
