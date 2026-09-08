import { useCallback, useEffect, useMemo, useState } from "react";
import { editorPrefs, rememberEditorPrefs, type EditorTheme } from "../../domain/editorPrefs";
import { challengeOf } from "../../domain/savedWork";
import { PanelHandle } from "./shell/PanelHandle";
import { useChallenge } from "../../hooks/useChallenge";
import { useLoadCurve } from "../../hooks/useLoadCurve";
import { useNarrowViewport } from "../../hooks/useMediaQuery";
import { usePageTitle } from "../../hooks/usePageTitle";
import { useLanguage } from "../../i18n";
import { applyFormat } from "../../i18n/format";
import type { Area, ArchitectureDto, ComponentLoad, FindingElement } from "../../types/architecture";
import { useEngine } from "../engine/useEngine";
import { Button } from "../ui/Button";
import { SpinnerIcon } from "../ui/Icons";
import { Segmented } from "../ui/Segmented";
import { Canvas } from "./canvas/Canvas";
import { isEntryPoint } from "./canvas/entryPoint";
import { lensTabs, lensView } from "./canvas/lens";
import { pointAt } from "./canvas/trafficDial";
import { NO_MARKS, type CanvasMarks, type EditorMode, type Selection } from "./canvas/types";
import { ViewStrip } from "./canvas/ViewStrip";
import { ZoomPill } from "./canvas/ZoomPill";
import { BriefBand } from "./challenge/BriefBand";
import { ChallengeJournal } from "./challenge/ChallengeJournal";
import { journalOf, type Journal } from "./challenge/journal";
import { ComponentSheet } from "./help/ComponentSheet";
import type { GlossControls } from "./help/FieldGloss";
import { EdgeInspector } from "./inspector/EdgeInspector";
import { NodeInspector } from "./inspector/NodeInspector";
import { EditorLoading } from "./overlays/EditorLoading";
import { EditorNotFound } from "./overlays/EditorNotFound";
import { ExportDialog } from "./overlays/ExportDialog";
import { SolutionCompare } from "./overlays/SolutionCompare";
import { AnalysisPanel, type FindingGraph } from "./panel/AnalysisPanel";
import { ImpactPanel } from "./panel/ImpactPanel";
import { callLookup } from "./panel/protagonist";
import { connectedComponents } from "./panel/scoreExplain";
import { elementOf, endsOf, titleOf } from "./selection";
import { BottomDrawer } from "./shell/BottomDrawer";
import { EditorBar, type BarChip } from "./shell/EditorBar";
import { NarrowMenu } from "./shell/NarrowMenu";
import { useShareLink } from "./shell/useShareLink";
import { DroppedFaults } from "./simulate/DroppedFaults";
import { FaultPicker, type FaultPickerProps } from "./simulate/FaultPicker";
import { FragilityMap } from "./simulate/FragilityMap";
import { Scenario } from "./simulate/Scenario";
import { SimulateColumn } from "./simulate/SimulateColumn";
import { useSimulate } from "./simulate/useSimulate";

interface ReadOnlyEditorProps {
  shareToken: string;
  search: string;
  onOpenCopy: () => void;
  copying: boolean;
  copyError: string | null;
}

const EMPTY_ARCHITECTURE: Pick<ArchitectureDto, "nodes" | "connections"> = { nodes: [], connections: [] };

const MODES: { value: EditorMode; label: string }[] = [
  { value: "design", label: "Design" },
  { value: "simulate", label: "Simulate" },
];

export function ReadOnlyEditor({ shareToken, search, onOpenCopy, copying, copyError }: ReadOnlyEditorProps) {
  const { locale, t } = useLanguage();
  const { state, retry } = useEngine(shareToken, null);
  const narrow = useNarrowViewport();
  const [selection, setSelection] = useState<Selection | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const challengeSlug = useMemo(() => challengeOf(search), [search]);
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
  const [exporting, setExporting] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [glossesOpen, setGlossesOpen] = useState(() => editorPrefs().glossesOpen);
  const [theme, setTheme] = useState<EditorTheme>(() => editorPrefs().theme);
  const [panelWidth, setPanelWidth] = useState(() => editorPrefs().panelWidth);
  const chooseTheme = useCallback((next: EditorTheme) => {
    rememberEditorPrefs({ theme: next });
    setTheme(next);
  }, []);
  const toggleTheme = useCallback(() => chooseTheme(theme === "light" ? "dark" : "light"), [chooseTheme, theme]);
  const resizePanel = useCallback((width: number) => {
    rememberEditorPrefs({ panelWidth: width });
    setPanelWidth(width);
  }, []);
  const glosses = useMemo<GlossControls>(
    () => ({
      open: glossesOpen,
      onToggle: () =>
        setGlossesOpen((current) => {
          rememberEditorPrefs({ glossesOpen: !current });
          return !current;
        }),
    }),
    [glossesOpen],
  );
  const selectedId = selection?.id ?? null;
  useEffect(() => {
    setSheetOpen(false);
  }, [selectedId]);
  const ready = state.status === "ready" ? state.data.architecture : null;
  const simulate = useSimulate({ shareToken, savedRevision: 0, architecture: ready ?? EMPTY_ARCHITECTURE, challenge: challenge.definition, ready: ready !== null });
  const { simulating } = simulate;
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
  const findingGraph = useMemo<FindingGraph>(
    () => ({ names: new Map((ready?.nodes ?? []).map((node) => [node.id, node.name])), calls: callLookup(ready?.connections ?? []) }),
    [ready],
  );
  const components = useMemo(() => (ready ? connectedComponents(ready.nodes, ready.connections) : 0), [ready]);
  const result = state.status === "ready" ? state.data.analysis : null;
  const lens = useMemo(() => lensView(result, lensArea), [result, lensArea]);
  const tabs = useMemo(() => lensTabs(result), [result]);
  const marks = useMemo<CanvasMarks>(() => {
    if (simulating) return NO_MARKS;
    const lensed = lens.analyzed && lens.area !== null;
    const dimmed = new Set<string>();
    if (lensed && ready) {
      for (const id of [...ready.nodes.map((node) => node.id), ...ready.connections.map((call) => call.id)]) if (!lens.ids.has(id) && !highlighted.has(id)) dimmed.add(id);
    }
    const badges = lensed ? lens.severities : showBadges ? lensView(result, null).severities : new Map();
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

  useEffect(() => {
    if (!simulating) return;
    closeDial();
    setPinned(null);
    setPreview(null);
  }, [simulating, closeDial]);

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

  const { architecture, analysis } = state.data;
  const chips: BarChip[] = [{ key: "readOnly", label: t("web.engine.readOnly") }, ...(challenge.definition ? [{ key: "challenge", label: t("ui.challenge.title") }] : [])];
  const selected = selection ? elementOf(architecture, selection) : null;
  const modeSwitch = <Segmented<EditorMode> options={MODES} value={simulate.mode} onChange={simulate.setMode} label={t("editor.bar.mode")} dark tap={narrow} />;
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
      mode={modeSwitch}
      primary={openCopy("sm")}
      notice={copyError}
      onShare={copy}
      shared={copied}
      onExport={() => setExporting(true)}
      onOpenMenu={() => setMenuOpen(true)}
      menuOpen={menuOpen}
      theme={theme}
      onToggleTheme={toggleTheme}
    />
  );
  const strip = simulating ? (
    <ViewStrip
      narrow={narrow}
      drawn={architecture.nodes.length > 0}
      legend={{ open: legendOpen, onToggle: toggleLegend, label: narrow ? undefined : t("editor.legend.whenBreaking") }}
      wave={simulate.wave.animatable ? { playing: simulate.wave.playing, onReplay: simulate.wave.replay, onSkip: simulate.wave.skip } : undefined}
      aside={<ZoomPill />}
    />
  ) : (
    <ViewStrip
      narrow={narrow}
      drawn={architecture.nodes.length > 0}
      lens={{ tabs, view: lens, onChange: setLensArea }}
      badges={{ on: showBadges, onToggle: () => setShowBadges((current) => !current) }}
      legend={{ open: legendOpen, onToggle: toggleLegend }}
      dial={{
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
      }}
      aside={<ZoomPill />}
    />
  );
  const canvas = (
    <Canvas
      architecture={architecture}
      states={simulating ? simulate.states : null}
      faultIds={simulate.faultIds}
      callLoad={simulating ? null : (analysis.callLoad ?? null)}
      selection={selection}
      onSelect={select}
      marks={marks}
      strip={strip}
      legend={{ mode: simulating ? "simulate" : "design", open: legendOpen, onClose: closeLegend }}
      narrow={narrow}
      emptyMessage={simulating ? t("editor.canvas.nothingToBreak") : undefined}
      notice={simulate.dropped > 0 ? <DroppedFaults count={simulate.dropped} onDismiss={simulate.dismissDropped} /> : undefined}
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
  const impact = (
    <ImpactPanel
      status={simulate.simulation.status}
      result={simulate.simulation.result}
      error={simulate.simulation.error}
      onRetry={simulate.simulation.retry}
      previous={simulate.previous}
      nameOf={simulate.nameOf}
      budgetMs={simulate.budgetMs}
      wave={simulate.wave}
      narrow={narrow}
      fragility={<FragilityMap shareToken={shareToken} savedRevision={0} faults={simulate.elementFaults} nameOf={simulate.nameOf} onPreview={simulate.previewRow} onSelect={simulate.loadRow} narrow={narrow} />}
    />
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
  const overlays = (
    <>
      {solutionOpen && challenge.definition && (
        <SolutionCompare slug={challenge.definition.slug} referenceName={t("challenge.referenceSolutionName", { title: challenge.definition.title })} shareToken={shareToken} onClose={() => setSolutionOpen(false)} />
      )}
      {exporting && (
        <ExportDialog architecture={architecture} baseline={null} analysis={analysis} simulation={simulating ? simulate.simulation.result : null} faults={simulating ? simulate.elementFaults : undefined} narrow={narrow} onClose={() => setExporting(false)} />
      )}
    </>
  );
  const inspector = selected && (
    <div className="flex flex-col gap-3">
      {selected.kind === "node" ? (
        <NodeInspector node={selected.node} isEntryPoint={isEntryPoint(selected.node.id, architecture.nodes, architecture.connections)} heading={narrow} narrow={narrow} glosses={glosses} onOpenSheet={() => setSheetOpen(true)} />
      ) : (
        <EdgeInspector connection={selected.connection} {...endsOf(architecture, selected.connection)} heading={narrow} narrow={narrow} glosses={glosses} />
      )}
    </div>
  );
  const sheet = selected?.kind === "node" && sheetOpen ? <ComponentSheet type={selected.node.type} onBack={() => setSheetOpen(false)} narrow={narrow} /> : null;
  const picker: Omit<FaultPickerProps, "narrow"> | null = selected
    ? {
        target: selected.kind === "node" ? "Node" : "Connection",
        id: selected.kind === "node" ? selected.node.id : selected.connection.id,
        name: titleOf(selected, architecture, t),
        type: selected.kind === "node" ? selected.node.type : undefined,
        state: simulate.stateOf(selection),
        faults: simulate.faults,
        onChange: simulate.setFaults,
        loading: simulate.simulation.status === "loading",
      }
    : null;
  const scenario = { faults: simulate.faults, nameOf: simulate.nameOf, onChange: simulate.setFaults, status: simulate.simulation.status, error: simulate.simulation.error, onRetry: simulate.simulation.retry };
  const metrics = simulate.simulation.result?.metrics ?? null;
  const panelTitle = simulating ? t("ui.impact.title") : t("ui.analysis.title");

  if (narrow) {
    return (
      <div data-theme={theme} className="flex min-h-0 flex-1 flex-col bg-panel text-panel-ink">
        {shell}
        {band}
        {canvas}
        {simulating && <Scenario {...scenario} narrow />}
        <BottomDrawer
          title={selected ? titleOf(selected, architecture, t) : simulating ? `${panelTitle} · ${metrics ? applyFormat(metrics.blastRadius, "percent", locale) : "—"}` : `${panelTitle} · ${analysis.score ?? "—"}`}
          open={drawerOpen}
          onToggle={() => setDrawerOpen((open) => !open)}
          aside={
            selected ? (
              <Button variant="panel" size="sm" onClick={() => setSelection(null)} className="min-h-11">
                {t(simulating ? "editor.selection.backImpact" : "editor.selection.back")}
              </Button>
            ) : simulating && metrics ? (
              <span className="inline-flex h-[26px] shrink-0 items-center rounded-pill border border-panel-line px-2.5 font-mono text-[11px] uppercase tracking-[0.06em] text-panel-ink-2">{t("editor.impact.downChip", { count: metrics.componentsDown })}</span>
            ) : undefined
          }
        >
          {selected ? (simulating && picker ? <FaultPicker {...picker} narrow /> : (sheet ?? inspector)) : simulating ? impact : motor}
        </BottomDrawer>
        {menuOpen && <NarrowMenu shareToken={shareToken} onClose={closeMenu} primary={openCopy("md")} onShare={copy} shared={copied} onExport={() => setExporting(true)} theme={theme} onTheme={chooseTheme} />}
        {overlays}
      </div>
    );
  }

  return (
    <div data-theme={theme} className="flex min-h-0 flex-1 flex-col bg-panel text-panel-ink">
      {shell}
      {band}
      <div className="flex min-h-0 flex-1">
        {simulating ? (
          <SimulateColumn scenario={scenario} picker={picker} />
        ) : (
          inspector && (
            <aside className="flex w-[264px] shrink-0 flex-col border-r border-panel-line bg-panel">
              <div className="flex h-11 items-center justify-between border-b border-panel-line px-3">
                <span className="eyebrow text-panel-ink-3">{sheet ? t("editor.help.sheet") : selected?.kind === "node" ? t("editor.inspector.component") : t("ui.inspector.call")}</span>
                <button type="button" onClick={() => setSelection(null)} className="rounded-pill px-2 py-1 text-meta text-panel-ink-2 hover:text-panel-ink">
                  {t("ui.close")}
                </button>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto p-3">{sheet ?? inspector}</div>
            </aside>
          )
        )}
        {canvas}
        <aside className="relative flex shrink-0 flex-col border-l border-panel-line bg-panel" style={{ width: panelWidth }}>
          <PanelHandle width={panelWidth} onChange={resizePanel} />
          <div className="flex h-11 items-center justify-between border-b border-panel-line px-3">
            <h2 className="font-display text-panel font-semibold text-panel-ink">{panelTitle}</h2>
            {simulating && simulate.simulation.status === "loading" && <SpinnerIcon size={12} className="text-panel-ink-3" />}
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3">{simulating ? impact : motor}</div>
        </aside>
      </div>
      {overlays}
    </div>
  );
}
