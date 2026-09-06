import { useCallback, useEffect, useMemo, useState } from "react";
import { editorPrefs, rememberEditorPrefs } from "../../domain/editorPrefs";
import { scenarioFromSearch } from "../../domain/scenarioUrl";
import { useChallenge } from "../../hooks/useChallenge";
import { useLoadCurve } from "../../hooks/useLoadCurve";
import { useNarrowViewport } from "../../hooks/useMediaQuery";
import { usePageTitle } from "../../hooks/usePageTitle";
import { useT } from "../../i18n";
import type { Area, ChallengeDetail, ComponentLoad, FindingElement } from "../../types/architecture";
import { useEngine, type EngineData } from "../engine/useEngine";
import { Button } from "../ui/Button";
import { Canvas } from "./canvas/Canvas";
import { isEntryPoint } from "./canvas/entryPoint";
import { lensTabs, lensView } from "./canvas/lens";
import { pointAt } from "./canvas/trafficDial";
import type { CanvasMarks, Selection } from "./canvas/types";
import { ViewStrip } from "./canvas/ViewStrip";
import { ZoomPill } from "./canvas/ZoomPill";
import { BriefBand } from "./challenge/BriefBand";
import { ChallengeJournal } from "./challenge/ChallengeJournal";
import { journalOf, type Journal } from "./challenge/journal";
import { EdgeInspector } from "./inspector/EdgeInspector";
import { NodeInspector } from "./inspector/NodeInspector";
import { EditorLoading } from "./overlays/EditorLoading";
import { EditorNotFound } from "./overlays/EditorNotFound";
import { SolutionCompare } from "./overlays/SolutionCompare";
import { AnalysisPanel, type FindingGraph } from "./panel/AnalysisPanel";
import { callLookup } from "./panel/protagonist";
import { connectedComponents } from "./panel/scoreExplain";
import { BottomDrawer } from "./shell/BottomDrawer";
import { EditorBar, type BarChip } from "./shell/EditorBar";
import { NarrowMenu } from "./shell/NarrowMenu";
import { useShareLink } from "./shell/useShareLink";
import { elementOf, endsOf, titleOf } from "./selection";

interface ReadOnlyEditorProps {
  shareToken: string;
  search: string;
  onOpenCopy: () => void;
  copying: boolean;
  copyError: string | null;
}

export function ReadOnlyEditor({ shareToken, search, onOpenCopy, copying, copyError }: ReadOnlyEditorProps) {
  const t = useT();
  const faults = useMemo(() => scenarioFromSearch(search), [search]);
  const { state, retry } = useEngine(shareToken, faults);
  const narrow = useNarrowViewport();
  const [selection, setSelection] = useState<Selection | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const challengeSlug = useMemo(() => new URLSearchParams(search).get("challenge"), [search]);
  const challenge = useChallenge({ slug: challengeSlug, shareToken, own: false });
  const [briefCollapsed, setBriefCollapsed] = useState(() => narrow || editorPrefs().briefCollapsed);
  const [solutionOpen, setSolutionOpen] = useState(false);
  const [preview, setPreview] = useState<FindingElement[] | null>(null);
  const [pinned, setPinned] = useState<{ id: string; elements: FindingElement[] } | null>(null);
  const highlighted = useMemo(() => new Set((preview ?? pinned?.elements ?? []).map((element) => element.id)), [preview, pinned]);
  const [legendOpen, setLegendOpen] = useState(() => editorPrefs().legendOpen);
  const [lensArea, setLensArea] = useState<Area | null>(null);
  const [showBadges, setShowBadges] = useState(false);
  const [dialOpen, setDialOpen] = useState(false);
  const [dialIndex, setDialIndex] = useState(0);
  const simulating = faults.length > 0;
  const loadCurve = useLoadCurve({ shareToken, enabled: dialOpen && !simulating, savedRevision: 0 });
  const trafficPoint = dialOpen && dialIndex > 0 ? pointAt(loadCurve.curve, dialIndex) : null;
  const toggleLegend = useCallback(() => {
    setLegendOpen((current) => {
      rememberEditorPrefs({ legendOpen: !current });
      return !current;
    });
  }, []);
  const closeLegend = useCallback(() => {
    setLegendOpen(false);
    rememberEditorPrefs({ legendOpen: false });
  }, []);
  const closeDial = useCallback(() => {
    setDialOpen(false);
    setDialIndex(0);
  }, []);
  const { copy, copied } = useShareLink(`${window.location.origin}/a/${encodeURIComponent(shareToken)}${search}`);
  usePageTitle(state.status === "ready" ? state.data.architecture.name : t("web.engine.readOnly"));
  const ready = state.status === "ready" ? state.data.architecture : null;
  const findingGraph = useMemo<FindingGraph>(
    () => ({ names: new Map((ready?.nodes ?? []).map((node) => [node.id, node.name])), calls: callLookup(ready?.connections ?? []) }),
    [ready],
  );
  const components = useMemo(() => (ready ? connectedComponents(ready.nodes, ready.connections) : 0), [ready]);
  const result = state.status === "ready" ? state.data.analysis : null;
  const lens = useMemo(() => lensView(result, lensArea), [result, lensArea]);
  const tabs = useMemo(() => lensTabs(result), [result]);
  const marks = useMemo<CanvasMarks>(() => {
    const lensed = !simulating && lens.analyzed && lens.area !== null;
    const dimmed = new Set<string>();
    if (lensed && ready) {
      for (const id of [...ready.nodes.map((node) => node.id), ...ready.connections.map((call) => call.id)]) if (!lens.ids.has(id) && !highlighted.has(id)) dimmed.add(id);
    }
    const badges = simulating ? new Map() : lensed ? lens.severities : showBadges ? lensView(result, null).severities : new Map();
    const loads = new Map<string, ComponentLoad>();
    for (const load of trafficPoint?.components ?? result?.load ?? []) loads.set(load.nodeId, load);
    return { highlighted, dimmed, badges, loads, loadScale: trafficPoint?.scale ?? 1 };
  }, [simulating, lens, ready, highlighted, showBadges, result, trafficPoint]);

  const journal = useMemo<Journal | null>(() => {
    if (!challenge.definition || !challenge.result?.passed || !ready) return null;
    return journalOf({ definition: challenge.definition, startingPoint: null, current: ready, first: null, analysis: result });
  }, [challenge.definition, challenge.result, ready, result]);

  const toggleBrief = useCallback(() => {
    setBriefCollapsed((current) => {
      if (!narrow) rememberEditorPrefs({ briefCollapsed: !current });
      return !current;
    });
  }, [narrow]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setSelection(null);
        setPinned(null);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const select = useCallback((next: Selection | null) => {
    setSelection(next);
    if (next) setDrawerOpen(true);
  }, []);

  const closeMenu = useCallback(() => setMenuOpen(false), []);
  const handlePin = useCallback((id: string, elements: FindingElement[]) => {
    setPinned((current) => (current?.id === id ? null : { id, elements }));
  }, []);

  if (state.status === "loading") return <EditorLoading />;
  if (state.status === "error") return <EditorNotFound message={state.message} onRetry={retry} />;

  const { architecture, analysis, simulation } = state.data;
  const faultIds = new Set(state.data.faults.flatMap((fault) => ("id" in fault ? [fault.id] : [])));
  const chips = chipsOf(state.data, challenge.definition, t);
  const selected = selection ? elementOf(architecture, selection) : null;
  const openCopy = (size: "sm" | "md") => (
    <Button variant="panelPrimary" size={size} onClick={onOpenCopy} loading={copying} loadingLabel={t("ui.editor.openingCopy")} className={size === "md" ? "w-full" : undefined}>
      {t("ui.editor.openCopy")}
    </Button>
  );
  const shell = (
    <EditorBar
      name={architecture.name}
      shareToken={shareToken}
      chips={chips}
      narrow={narrow}
      primary={openCopy("sm")}
      notice={copyError}
      onShare={copy}
      shared={copied}
      onOpenMenu={() => setMenuOpen(true)}
      menuOpen={menuOpen}
    />
  );
  const canvas = (
    <Canvas
      architecture={architecture}
      states={simulation?.states ?? null}
      faultIds={faultIds}
      callLoad={analysis.callLoad ?? null}
      selection={selection}
      onSelect={select}
      marks={marks}
      strip={
        <ViewStrip
          narrow={narrow}
          drawn={architecture.nodes.length > 0}
          lens={simulating ? undefined : { tabs, view: lens, onChange: setLensArea }}
          badges={simulating ? undefined : { on: showBadges, onToggle: () => setShowBadges((current) => !current) }}
          legend={{ open: legendOpen, onToggle: toggleLegend }}
          dial={
            simulating
              ? undefined
              : {
                  status: loadCurve.status,
                  curve: loadCurve.curve,
                  error: loadCurve.error,
                  index: dialIndex,
                  scale: trafficPoint?.scale ?? 1,
                  nameOf: (nodeId) => architecture.nodes.find((node) => node.id === nodeId)?.name ?? "?",
                  onIndexChange: setDialIndex,
                  onOpen: () => setDialOpen(true),
                  onClose: closeDial,
                  onRetry: loadCurve.retry,
                }
          }
          aside={<ZoomPill />}
        />
      }
      legend={{ mode: simulating ? "simulate" : "design", open: legendOpen, onClose: closeLegend }}
      narrow={narrow}
    />
  );
  const motor = (
    <div className="flex flex-col gap-[11px]">
      <AnalysisPanel
        status="idle"
        result={analysis}
        error={null}
        onPreview={setPreview}
        onPin={handlePin}
        pinnedId={pinned?.id ?? null}
        components={components}
        shareToken={shareToken}
        graph={findingGraph}
        narrow={narrow}
      />
      {journal && <ChallengeJournal journal={journal} own={false} onCompare={() => setSolutionOpen(true)} onShare={copy} shared={copied} narrow={narrow} />}
    </div>
  );
  const band = challenge.definition && (
    <BriefBand
      key={challenge.definition.slug}
      definition={challenge.definition}
      result={challenge.result}
      status={challenge.status}
      error={challenge.error}
      collapsed={briefCollapsed}
      onToggle={toggleBrief}
      onEvaluate={() => void challenge.evaluate()}
      own={false}
      narrow={narrow}
      actions={{ onPreview: setPreview, onPin: handlePin, pinnedId: pinned?.id ?? null }}
    />
  );
  const overlays = solutionOpen && challenge.definition && (
    <SolutionCompare slug={challenge.definition.slug} referenceName={t("challenge.referenceSolutionName", { title: challenge.definition.title })} shareToken={shareToken} onClose={() => setSolutionOpen(false)} />
  );
  const inspector = selected && (
    <div className="flex flex-col gap-3">
      {selected.kind === "node" ? (
        <NodeInspector node={selected.node} isEntryPoint={isEntryPoint(selected.node.id, architecture.nodes, architecture.connections)} heading={narrow} />
      ) : (
        <EdgeInspector connection={selected.connection} {...endsOf(architecture, selected.connection)} heading={narrow} />
      )}
    </div>
  );

  if (narrow) {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        {shell}
        {band}
        {canvas}
        <BottomDrawer
          title={selected ? titleOf(selected, architecture, t) : `${t("ui.analysis.title")} · ${analysis.score ?? "—"}`}
          open={drawerOpen}
          onToggle={() => setDrawerOpen((open) => !open)}
          aside={
            selected ? (
              <Button variant="panel" size="sm" onClick={() => setSelection(null)} className="min-h-11">
                {t("editor.selection.back")}
              </Button>
            ) : undefined
          }
        >
          {inspector ?? motor}
        </BottomDrawer>
        {menuOpen && <NarrowMenu shareToken={shareToken} onClose={closeMenu} primary={openCopy("md")} onShare={copy} shared={copied} />}
        {overlays}
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {shell}
      {band}
      <div className="flex min-h-0 flex-1">
        {inspector && (
          <aside className="flex w-[264px] shrink-0 flex-col border-r border-panel-line bg-panel">
            <div className="flex h-11 items-center justify-between border-b border-panel-line px-3">
              <span className="eyebrow text-panel-ink-3">{selected?.kind === "node" ? t("editor.inspector.component") : t("ui.inspector.call")}</span>
              <button type="button" onClick={() => setSelection(null)} className="rounded-pill px-2 py-1 text-meta text-panel-ink-2 hover:text-panel-ink">
                {t("ui.close")}
              </button>
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto p-3">{inspector}</div>
          </aside>
        )}
        {canvas}
        <aside className="flex w-[336px] shrink-0 flex-col border-l border-panel-line bg-panel">
          <div className="flex h-11 items-center border-b border-panel-line px-3">
            <h2 className="font-display text-panel font-semibold text-panel-ink">{t("ui.analysis.title")}</h2>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3">{motor}</div>
        </aside>
      </div>
      {overlays}
    </div>
  );
}

function chipsOf(data: EngineData, challenge: ChallengeDetail | null, t: ReturnType<typeof useT>): BarChip[] {
  const chips: BarChip[] = [{ key: "readOnly", label: t("web.engine.readOnly") }];
  if (challenge) chips.push({ key: "challenge", label: t("ui.challenge.title") });
  for (const fault of data.faults) {
    if (!("id" in fault)) continue;
    const node = data.architecture.nodes.find((candidate) => candidate.id === fault.id);
    if (node) chips.push({ key: `fault-${fault.id}`, label: t("web.engine.fault", { name: node.name }), variant: "critical" });
  }
  return chips;
}
