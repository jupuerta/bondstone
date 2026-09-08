import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getArchitectureHistory } from "../../api/architectureClient";
import { editorPrefs, rememberEditorPrefs, type EditorTheme } from "../../domain/editorPrefs";
import { PanelHandle } from "./shell/PanelHandle";
import { challengeOf, rememberWork, scoreWork, touchWork } from "../../domain/savedWork";
import { startingPointOf } from "../../domain/startingPoint";
import { useAutosave } from "../../hooks/useAutosave";
import { useChallenge } from "../../hooks/useChallenge";
import { useLiveAnalysis } from "../../hooks/useLiveAnalysis";
import { useLoadCurve } from "../../hooks/useLoadCurve";
import { useNarrowViewport } from "../../hooks/useMediaQuery";
import { usePageTitle } from "../../hooks/usePageTitle";
import { useLanguage } from "../../i18n";
import { applyFormat } from "../../i18n/format";
import type { AnalysisResult, Area, ArchitectureDto, ArchitectureSnapshot, ComponentLoad, FindingElement, NodeType, PositionDto, ResilienceConfiguration, RuleFinding, UpdateArchitectureResult } from "../../types/architecture";
import { Button } from "../ui/Button";
import { Segmented } from "../ui/Segmented";
import { SpinnerIcon } from "../ui/Icons";
import { applyFix } from "./canvas/applyFix";
import { Canvas, type CanvasEdits } from "./canvas/Canvas";
import { addNode, connect, connectedEdges, entryPointOf, importGraph, isTypingTarget, moveNodes, patchEdge, patchNode, removeEdges, removeNodes, setNodeField } from "./canvas/graphEdits";
import { EMPTY_HISTORY, record, redo, undo, type Graph, type GraphHistory } from "./canvas/graphHistory";
import { lensTabs, lensView } from "./canvas/lens";
import { flowToArchitecture, flowToUpdateRequest } from "./canvas/mapping";
import { pointAt } from "./canvas/trafficDial";
import { NO_MARKS, type CanvasMarks, type EditorMode, type Selection } from "./canvas/types";
import { ViewStrip } from "./canvas/ViewStrip";
import { ZoomPill } from "./canvas/ZoomPill";
import { BriefBand } from "./challenge/BriefBand";
import { ChallengeJournal } from "./challenge/ChallengeJournal";
import { journalOf, type Journal } from "./challenge/journal";
import { ComponentCard } from "./help/ComponentCard";
import { ComponentSheet } from "./help/ComponentSheet";
import type { GlossControls } from "./help/FieldGloss";
import { useComponentHelp } from "./help/useComponentHelp";
import { EdgeInspector } from "./inspector/EdgeInspector";
import { NodeInspector } from "./inspector/NodeInspector";
import { ConflictDialog } from "./overlays/ConflictDialog";
import { ExportDialog } from "./overlays/ExportDialog";
import { FixDiff, type FixShots } from "./overlays/FixDiff";
import { SolutionCompare } from "./overlays/SolutionCompare";
import { Palette } from "./palette/Palette";
import { AnalysisPanel, type FindingGraph, type FixControls } from "./panel/AnalysisPanel";
import { ImpactPanel } from "./panel/ImpactPanel";
import { patternForFinding } from "./panel/findingPattern";
import { scoreSnapshot, whatWasDone, type FixReceipt } from "./panel/fixReceipt";
import { callLookup } from "./panel/protagonist";
import { connectedComponents } from "./panel/scoreExplain";
import { elementOf, endsOf, titleOf } from "./selection";
import { BottomDrawer } from "./shell/BottomDrawer";
import { EditorBar, type BarChip } from "./shell/EditorBar";
import { NarrowMenu } from "./shell/NarrowMenu";
import { SaveState } from "./shell/SaveState";
import { useShareLink } from "./shell/useShareLink";
import { DroppedFaults } from "./simulate/DroppedFaults";
import { FaultPicker, type FaultPickerProps } from "./simulate/FaultPicker";
import { FragilityMap } from "./simulate/FragilityMap";
import { Scenario } from "./simulate/Scenario";
import { SimulateColumn } from "./simulate/SimulateColumn";
import { useSimulate } from "./simulate/useSimulate";
import { Starters } from "./starters/Starters";
import { starterToDto, type StarterArchitecture } from "./starters/starterArchitectures";

interface EditorProps {
  architecture: ArchitectureDto;
  editToken: string;
  search: string;
  onReload: () => void;
}

interface Pinned {
  id: string;
  elements: FindingElement[];
}

const MODES: { value: EditorMode; label: string }[] = [
  { value: "design", label: "Design" },
  { value: "simulate", label: "Simulate" },
];

export function Editor({ architecture, editToken, search, onReload }: EditorProps) {
  const { locale, t } = useLanguage();
  const narrow = useNarrowViewport();
  const editable = !narrow;

  const [graph, setGraph] = useState<Graph>(() => importGraph(architecture.nodes, architecture.connections));
  const [name, setName] = useState(architecture.name);
  const [version, setVersion] = useState(architecture.version);
  const [selection, setSelection] = useState<Selection | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => editorPrefs().paletteCollapsed);
  const [legendOpen, setLegendOpen] = useState(() => editorPrefs().legendOpen);
  const [lensArea, setLensArea] = useState<Area | null>(null);
  const [showBadges, setShowBadges] = useState(false);
  const [dialOpen, setDialOpen] = useState(false);
  const [dialIndex, setDialIndex] = useState(0);
  const [conflict, setConflict] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [glossesOpen, setGlossesOpen] = useState(() => editorPrefs().glossesOpen);
  const [theme, setTheme] = useState<EditorTheme>(() => editorPrefs().theme);
  const [panelWidth, setPanelWidth] = useState(() => editorPrefs().panelWidth);
  const help = useComponentHelp();
  const canvasBox = useRef<HTMLDivElement>(null);
  const [savedRevision, setSavedRevision] = useState(0);
  const challengeSlug = useMemo(() => challengeOf(search), [search]);
  const challenge = useChallenge({ slug: challengeSlug, shareToken: architecture.shareToken, own: true });
  const [briefCollapsed, setBriefCollapsed] = useState(() => narrow || editorPrefs().briefCollapsed);
  const [startingPoint] = useState(() => startingPointOf(challengeSlug));
  const [solutionOpen, setSolutionOpen] = useState(false);
  const [evaluateQueued, setEvaluateQueued] = useState(false);
  const evaluateAfterSave = useRef(false);
  const evaluateRef = useRef(challenge.evaluate);
  evaluateRef.current = challenge.evaluate;
  const { copy, copied } = useShareLink(`${window.location.origin}/a/${encodeURIComponent(architecture.shareToken)}${search}`);
  usePageTitle(name);

  const graphRef = useRef(graph);
  const nameRef = useRef(name);
  const versionRef = useRef(version);
  const historyRef = useRef<GraphHistory>(EMPTY_HISTORY);
  graphRef.current = graph;
  nameRef.current = name;
  versionRef.current = version;

  const dto = useMemo(() => flowToArchitecture(architecture, name, version, graph.nodes, graph.edges), [architecture, name, version, graph]);

  const simulate = useSimulate({ shareToken: architecture.shareToken, savedRevision, architecture: dto, challenge: challenge.definition });
  const { simulating } = simulate;
  const canEdit = editable && !simulating;

  useEffect(() => {
    rememberWork({ editToken, shareToken: architecture.shareToken, name: architecture.name, challengeSlug });
  }, [editToken, architecture.shareToken, architecture.name, challengeSlug]);

  const getSnapshot = useCallback(() => flowToUpdateRequest(nameRef.current, versionRef.current, graphRef.current.nodes, graphRef.current.edges), []);
  const onSaved = useCallback(
    (result: UpdateArchitectureResult) => {
      setVersion(result.version);
      setSavedRevision((current) => current + 1);
      touchWork(editToken, { name: nameRef.current });
    },
    [editToken],
  );
  const onConflict = useCallback(() => {
    evaluateAfterSave.current = false;
    setEvaluateQueued(false);
    setConflict(true);
  }, []);
  const autosave = useAutosave({ editToken, enabled: editable, getSnapshot, onSaved, onConflict });

  const lastScheduled = useRef({ graph, name });
  const { scheduleSave } = autosave;
  useEffect(() => {
    if (lastScheduled.current.graph === graph && lastScheduled.current.name === name) return;
    lastScheduled.current = { graph, name };
    scheduleSave();
  }, [graph, name, scheduleSave]);

  const invalidateChallenge = challenge.invalidate;
  useEffect(() => {
    invalidateChallenge();
    if (!evaluateAfterSave.current) return;
    evaluateAfterSave.current = false;
    setEvaluateQueued(false);
    void evaluateRef.current();
  }, [savedRevision, invalidateChallenge]);

  const analysis = useLiveAnalysis({ shareToken: architecture.shareToken, savedRevision });
  useEffect(() => {
    if (analysis.result) scoreWork(editToken, analysis.result.score);
  }, [analysis.result, editToken]);

  const [history, setHistory] = useState<ArchitectureSnapshot[]>([]);
  const [historyRevision, setHistoryRevision] = useState(0);
  const scoringVersion = analysis.result?.scoringVersion;
  useEffect(() => {
    if (!scoringVersion) return;
    let cancelled = false;
    getArchitectureHistory(architecture.shareToken)
      .then((entries) => {
        if (!cancelled) setHistory(entries);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [architecture.shareToken, scoringVersion, historyRevision]);

  const comparable = useMemo(() => history.filter((entry) => entry.scoringVersion === scoringVersion), [history, scoringVersion]);
  const lastPlotted = comparable.length > 0 ? comparable[comparable.length - 1].score : null;
  const liveScore = analysis.result?.score ?? null;
  useEffect(() => {
    if (savedRevision === 0 || analysis.status !== "idle" || analysis.result === null) return;
    if (liveScore === lastPlotted) return;
    setHistoryRevision((current) => current + 1);
  }, [savedRevision, liveScore, lastPlotted, analysis.status, analysis.result]);

  const commit = useCallback((update: (current: Graph) => Graph) => {
    const current = graphRef.current;
    const next = update(current);
    if (next === current) return;
    historyRef.current = record(historyRef.current, current);
    graphRef.current = next;
    setGraph(next);
  }, []);

  const step = useCallback((direction: typeof undo) => {
    const stepped = direction(historyRef.current, graphRef.current);
    if (!stepped) return;
    historyRef.current = stepped.history;
    graphRef.current = stepped.graph;
    setGraph(stepped.graph);
  }, []);

  const [preview, setPreview] = useState<FindingElement[] | null>(null);
  const [pinned, setPinned] = useState<Pinned | null>(null);
  const highlighted = useMemo(() => new Set((preview ?? pinned?.elements ?? []).map((element) => element.id)), [preview, pinned]);

  const loadCurve = useLoadCurve({ shareToken: architecture.shareToken, enabled: dialOpen && !simulating, savedRevision });
  const trafficPoint = dialOpen && dialIndex > 0 ? pointAt(loadCurve.curve, dialIndex) : null;
  const lens = useMemo(() => lensView(analysis.result, lensArea), [analysis.result, lensArea]);
  const tabs = useMemo(() => lensTabs(analysis.result), [analysis.result]);
  const marks = useMemo<CanvasMarks>(() => {
    if (simulating) return NO_MARKS;
    const lensed = lens.analyzed && lens.area !== null;
    const dimmed = new Set<string>();
    if (lensed) {
      for (const id of [...dto.nodes.map((node) => node.id), ...dto.connections.map((call) => call.id)]) if (!lens.ids.has(id) && !highlighted.has(id)) dimmed.add(id);
    }
    const badges = lensed ? lens.severities : showBadges ? lensView(analysis.result, null).severities : new Map();
    const loads = new Map<string, ComponentLoad>();
    for (const load of trafficPoint?.components ?? analysis.result?.load ?? []) loads.set(load.nodeId, load);
    return { highlighted, dimmed, badges, loads, loadScale: trafficPoint?.scale ?? 1 };
  }, [simulating, lens, dto, highlighted, showBadges, analysis.result, trafficPoint]);
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
  const handlePin = useCallback((id: string, elements: FindingElement[]) => {
    setPinned((current) => (current?.id === id ? null : { id, elements }));
  }, []);

  const saveStatus = autosave.status;
  const { retryNow } = autosave;
  const { evaluate } = challenge;
  const requestEvaluate = useCallback(() => {
    if (saveStatus === "pending" || saveStatus === "saving") {
      evaluateAfterSave.current = true;
      setEvaluateQueued(true);
      if (saveStatus === "pending") retryNow();
      return;
    }
    void evaluate();
  }, [saveStatus, retryNow, evaluate]);
  const toggleBrief = useCallback(() => {
    setBriefCollapsed((current) => {
      if (!narrow) rememberEditorPrefs({ briefCollapsed: !current });
      return !current;
    });
  }, [narrow]);

  const journal = useMemo<Journal | null>(() => {
    if (!challenge.definition || !challenge.result?.passed) return null;
    return journalOf({ definition: challenge.definition, startingPoint, current: dto, first: comparable.length > 0 ? comparable[0] : null, analysis: analysis.result });
  }, [challenge.definition, challenge.result, startingPoint, dto, comparable, analysis.result]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setPinned(null);
        if (!isTypingTarget(event.target)) setSelection(null);
        return;
      }
      if (!canEdit || !(event.ctrlKey || event.metaKey) || event.altKey) return;
      const key = event.key.toLowerCase();
      if (key !== "z" && key !== "y") return;
      if (isTypingTarget(event.target)) return;
      event.preventDefault();
      step(key === "y" || event.shiftKey ? redo : undo);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [canEdit, step]);

  const [receipt, setReceipt] = useState<FixReceipt | null>(null);
  const [shots, setShots] = useState<FixShots | null>(null);
  const [compareOpen, setCompareOpen] = useState(false);
  const undoRef = useRef<Graph | null>(null);
  const fixedAtRef = useRef(0);
  const analysisBeforeFixRef = useRef<AnalysisResult | null>(null);

  const clearFix = useCallback(() => {
    undoRef.current = null;
    setReceipt(null);
    setShots(null);
    setCompareOpen(false);
  }, []);

  const handleApplyFix = useCallback(
    (finding: RuleFinding) => {
      if (!finding.fix) return;
      const before = graphRef.current;
      const next = applyFix(before, finding.fix);
      commit(() => next);
      setPinned(null);
      setPreview(null);

      undoRef.current = before;
      fixedAtRef.current = savedRevision;
      analysisBeforeFixRef.current = analysis.result;

      const shot = (current: Graph) => {
        const { nodes, connections } = flowToUpdateRequest(nameRef.current, versionRef.current, current.nodes, current.edges);
        return { nodes, connections };
      };
      setShots({ before: shot(before), after: shot(next) });
      setCompareOpen(true);
      setReceipt({ area: finding.area, code: finding.code, done: whatWasDone(finding.fix), pattern: patternForFinding(finding), before: scoreSnapshot(analysis.result), after: null });
    },
    [commit, savedRevision, analysis.result],
  );

  useEffect(() => {
    if (!receipt) return;
    if (savedRevision > fixedAtRef.current + 1) {
      clearFix();
      return;
    }
    if (receipt.after !== null || savedRevision !== fixedAtRef.current + 1) return;
    if (analysis.status !== "idle" || analysis.result === null) return;
    if (analysis.result === analysisBeforeFixRef.current) return;
    const after = scoreSnapshot(analysis.result);
    setReceipt((current) => (current && current.after === null ? { ...current, after } : current));
  }, [receipt, savedRevision, analysis.status, analysis.result, clearFix]);

  const handleUndoFix = useCallback(() => {
    const previous = undoRef.current;
    clearFix();
    if (previous) commit(() => previous);
  }, [clearFix, commit]);

  const closeHelp = help.close;
  useEffect(() => {
    if (!simulating) return;
    clearFix();
    closeDial();
    closeHelp();
    setPinned(null);
    setPreview(null);
  }, [simulating, clearFix, closeDial, closeHelp]);

  const selectedId = selection?.id ?? null;
  useEffect(() => {
    setSheetOpen(false);
  }, [selectedId]);

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

  const openCompare = useCallback(() => setCompareOpen(true), []);
  const closeCompare = useCallback(() => setCompareOpen(false), []);

  const select = useCallback((next: Selection | null) => {
    setSelection(next);
    if (next) setDrawerOpen(true);
  }, []);
  const closeMenu = useCallback(() => setMenuOpen(false), []);
  const togglePalette = useCallback(() => {
    setPaletteCollapsed((current) => {
      rememberEditorPrefs({ paletteCollapsed: !current });
      return !current;
    });
  }, []);
  const chooseTheme = useCallback((next: EditorTheme) => {
    rememberEditorPrefs({ theme: next });
    setTheme(next);
  }, []);
  const toggleTheme = useCallback(() => chooseTheme(theme === "light" ? "dark" : "light"), [chooseTheme, theme]);
  const resizePanel = useCallback((width: number) => {
    rememberEditorPrefs({ panelWidth: width });
    setPanelWidth(width);
  }, []);

  const edits = useMemo<CanvasEdits>(
    () => ({
      onAddNode: (type: NodeType, position: PositionDto) => commit((current) => addNode(current, type, position)),
      onConnect: (source, target) => commit((current) => connect(current, source, target)),
      onMove: (positions) => commit((current) => moveNodes(current, positions)),
      onDelete: (nodeIds, edgeIds) => {
        commit((current) => removeEdges(removeNodes(current, nodeIds), edgeIds));
        setSelection(null);
      },
    }),
    [commit],
  );

  const pickStarter = useCallback(
    (starter: StarterArchitecture) => {
      const { nodes, connections } = starterToDto(starter);
      commit(() => importGraph(nodes, connections));
    },
    [commit],
  );

  const selected = selection ? elementOf(dto, selection) : null;
  const chips: BarChip[] = challenge.definition ? [{ key: "challenge", label: t("ui.challenge.title") }] : [];
  const saveState = <SaveState status={autosave.status} version={version} error={autosave.error} onRetry={autosave.retryNow} narrow={narrow} />;
  const modeSwitch = <Segmented<EditorMode> options={MODES} value={simulate.mode} onChange={simulate.setMode} label={t("editor.bar.mode")} dark tap={narrow} />;

  const findingGraph = useMemo<FindingGraph>(
    () => ({ names: new Map(dto.nodes.map((node) => [node.id, node.name])), calls: callLookup(dto.connections) }),
    [dto],
  );
  const components = useMemo(() => connectedComponents(dto.nodes, dto.connections), [dto]);
  const fixes: FixControls | undefined = canEdit
    ? { onApplyFix: handleApplyFix, receipt, onUndoFix: handleUndoFix, onDismissFix: clearFix, onOpenFixCompare: shots ? openCompare : null }
    : undefined;

  const inspector = selected && (
    <div className="flex flex-col gap-3">
      {selected.kind === "node" ? (
        <NodeInspector
          node={selected.node}
          isEntryPoint={entryPointOf(graph, selected.node.id)}
          heading={narrow}
          narrow={narrow}
          glosses={glosses}
          onOpenSheet={() => setSheetOpen(true)}
          edits={
            canEdit
              ? {
                  connectedEdges: connectedEdges(graph, selected.node.id),
                  onRename: (next) => commit((current) => patchNode(current, selected.node.id, { name: next })),
                  onField: (key, value) => commit((current) => setNodeField(current, selected.node.id, key, value)),
                  onDelete: () => edits.onDelete(new Set([selected.node.id]), new Set()),
                }
              : undefined
          }
        />
      ) : (
        <EdgeInspector
          connection={selected.connection}
          {...endsOf(dto, selected.connection)}
          heading={narrow}
          narrow={narrow}
          glosses={glosses}
          edits={
            canEdit
              ? {
                  onSynchronous: (synchronous) => commit((current) => patchEdge(current, selected.connection.id, { synchronous })),
                  onResilience: (patch: ResilienceConfiguration) =>
                    commit((current) => {
                      const edge = current.edges.find((candidate) => candidate.id === selected.connection.id);
                      const next: ResilienceConfiguration = { ...(edge?.data?.configuration ?? {}), ...patch };
                      for (const [key, value] of Object.entries(next)) if (value === null || value === undefined) delete next[key as keyof ResilienceConfiguration];
                      return patchEdge(current, selected.connection.id, { configuration: next });
                    }),
                  onDelete: () => edits.onDelete(new Set(), new Set([selected.connection.id])),
                }
              : undefined
          }
        />
      )}
    </div>
  );

  const sheet = selected?.kind === "node" && sheetOpen ? <ComponentSheet type={selected.node.type} onBack={() => setSheetOpen(false)} narrow={narrow} /> : null;

  const picker: Omit<FaultPickerProps, "narrow"> | null = selected
    ? {
        target: selected.kind === "node" ? "Node" : "Connection",
        id: selected.kind === "node" ? selected.node.id : selected.connection.id,
        name: titleOf(selected, dto, t),
        type: selected.kind === "node" ? selected.node.type : undefined,
        state: simulate.stateOf(selection),
        faults: simulate.faults,
        onChange: simulate.setFaults,
        loading: simulate.simulation.status === "loading",
      }
    : null;

  const scenario = { faults: simulate.faults, nameOf: simulate.nameOf, onChange: simulate.setFaults, status: simulate.simulation.status, error: simulate.simulation.error, onRetry: simulate.simulation.retry };

  const motor = (
    <div className="flex flex-col gap-[11px]">
      <AnalysisPanel
        status={analysis.status}
        result={analysis.result}
        error={analysis.error}
        onRetry={analysis.retry}
        onPreview={setPreview}
        onPin={handlePin}
        pinnedId={pinned?.id ?? null}
        fixes={fixes}
        history={{ entries: comparable, olderScoring: history.length - comparable.length }}
        components={components}
        shareToken={architecture.shareToken}
        graph={findingGraph}
        narrow={narrow}
      />
      {journal && <ChallengeJournal journal={journal} own onCompare={() => setSolutionOpen(true)} onShare={copy} shared={copied} narrow={narrow} />}
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
      fragility={<FragilityMap shareToken={architecture.shareToken} savedRevision={savedRevision} faults={simulate.elementFaults} nameOf={simulate.nameOf} onPreview={simulate.previewRow} onSelect={simulate.loadRow} narrow={narrow} />}
    />
  );

  const band = challenge.definition && (
    <BriefBand
      key={challenge.definition.slug}
      definition={challenge.definition}
      result={challenge.result}
      status={evaluateQueued ? "evaluating" : challenge.status}
      error={challenge.error}
      collapsed={briefCollapsed}
      onToggle={toggleBrief}
      onEvaluate={requestEvaluate}
      own
      narrow={narrow}
      actions={{ onPreview: setPreview, onPin: handlePin, pinnedId: pinned?.id ?? null }}
    />
  );

  const shell = (
    <EditorBar
      name={name}
      onRename={canEdit ? setName : undefined}
      shareToken={architecture.shareToken}
      chips={chips}
      narrow={narrow}
      status={saveState}
      mode={modeSwitch}
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
      drawn={dto.nodes.length > 0}
      legend={{ open: legendOpen, onToggle: toggleLegend, label: narrow ? undefined : t("editor.legend.whenBreaking") }}
      wave={simulate.wave.animatable ? { playing: simulate.wave.playing, onReplay: simulate.wave.replay, onSkip: simulate.wave.skip } : undefined}
      aside={<ZoomPill />}
    />
  ) : (
    <ViewStrip
      narrow={narrow}
      drawn={dto.nodes.length > 0}
      lens={{ tabs, view: lens, onChange: setLensArea }}
      badges={{ on: showBadges, onToggle: () => setShowBadges((current) => !current) }}
      legend={{ open: legendOpen, onToggle: toggleLegend }}
      dial={{
        status: loadCurve.status,
        curve: loadCurve.curve,
        error: loadCurve.error,
        index: dialIndex,
        scale: trafficPoint?.scale ?? 1,
        nameOf: (nodeId) => dto.nodes.find((node) => node.id === nodeId)?.name ?? "?",
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
      architecture={dto}
      states={simulating ? simulate.states : null}
      faultIds={simulate.faultIds}
      callLoad={simulating ? null : (analysis.result?.callLoad ?? null)}
      selection={selection}
      onSelect={select}
      marks={marks}
      strip={strip}
      legend={{ mode: simulating ? "simulate" : "design", open: legendOpen, onClose: closeLegend }}
      narrow={narrow}
      edits={canEdit ? edits : undefined}
      emptyMessage={simulating ? t("editor.canvas.nothingToBreak") : undefined}
      notice={simulate.dropped > 0 ? <DroppedFaults count={simulate.dropped} onDismiss={simulate.dismissDropped} /> : undefined}
      empty={
        simulating ? undefined : canEdit ? (
          <Starters onPick={pickStarter} />
        ) : (
          <div className="pointer-events-auto flex flex-wrap justify-center gap-2">
            <Button variant="panel" size="sm" onClick={copy}>
              {copied ? t("ui.copied") : t("ui.share")}
            </Button>
            <Button variant="panel" size="sm" to="/challenges">
              {t("web.shared.challenges")}
            </Button>
          </div>
        )
      }
    />
  );

  const overlays = (
    <>
      {compareOpen && shots && receipt && <FixDiff shots={shots} receipt={receipt} onUndo={handleUndoFix} onClose={closeCompare} />}
      {solutionOpen && challenge.definition && (
        <SolutionCompare slug={challenge.definition.slug} referenceName={t("challenge.referenceSolutionName", { title: challenge.definition.title })} shareToken={architecture.shareToken} onClose={() => setSolutionOpen(false)} />
      )}
      {conflict && <ConflictDialog onReload={onReload} />}
      {exporting && (
        <ExportDialog
          architecture={dto}
          baseline={architecture}
          analysis={analysis.result}
          simulation={simulating ? simulate.simulation.result : null}
          faults={simulating ? simulate.elementFaults : undefined}
          narrow={narrow}
          onClose={() => setExporting(false)}
        />
      )}
    </>
  );

  const metrics = simulate.simulation.result?.metrics ?? null;
  const panelTitle = simulating ? t("ui.impact.title") : t("ui.analysis.title");
  const panelBusy = simulating ? simulate.simulation.status === "loading" : analysis.status === "loading";

  if (narrow) {
    return (
      <div data-theme={theme} className="flex min-h-0 flex-1 flex-col bg-panel text-panel-ink">
        {shell}
        {band}
        {!simulating && <p className="shrink-0 border-b border-panel-line bg-panel-3 px-3 py-2 text-meta text-panel-ink-2">{t("web.engine.narrow")}</p>}
        {canvas}
        {simulating && <Scenario {...scenario} narrow />}
        <BottomDrawer
          title={selected ? titleOf(selected, dto, t) : simulating ? `${panelTitle} · ${metrics ? applyFormat(metrics.blastRadius, "percent", locale) : "—"}` : `${panelTitle} · ${analysis.result?.score ?? "—"}`}
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
        {menuOpen && <NarrowMenu shareToken={architecture.shareToken} onClose={closeMenu} onShare={copy} shared={copied} onExport={() => setExporting(true)} theme={theme} onTheme={chooseTheme} />}
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
          <aside className={`flex shrink-0 border-r border-panel-line bg-panel ${inspector || paletteCollapsed ? "w-auto" : "w-[240px]"}`}>
            {inspector ? (
              <>
                <Palette collapsed onToggle={togglePalette} />
                <div className="flex w-[216px] flex-col">
                  <div className="flex h-11 shrink-0 items-center justify-between border-b border-panel-line px-3">
                    <span className="eyebrow text-panel-ink-3">{sheet ? t("editor.help.sheet") : selected?.kind === "node" ? t("editor.inspector.component") : t("ui.inspector.call")}</span>
                    <button type="button" onClick={() => setSelection(null)} className="rounded-pill px-2 py-1 text-meta text-panel-ink-2 hover:text-panel-ink">
                      {t("ui.close")}
                    </button>
                  </div>
                  <div className="min-h-0 flex-1 overflow-y-auto p-3">{sheet ?? inspector}</div>
                </div>
              </>
            ) : (
              <Palette collapsed={paletteCollapsed} onToggle={togglePalette} help={{ type: help.type, onEnter: help.enter, onFocus: help.focus, onLeave: help.leave, onClose: help.close }} />
            )}
          </aside>
        )}
        <div ref={canvasBox} className="relative flex min-h-0 min-w-0 flex-1">
          {canvas}
          {help.type !== null && canEdit && <ComponentCard type={help.type} canvas={canvasBox.current} anchorTop={help.anchorTop} onMouseEnter={help.keep} onMouseLeave={help.leaveCard} />}
        </div>
        <aside className="relative flex shrink-0 flex-col border-l border-panel-line bg-panel" style={{ width: panelWidth }}>
          <PanelHandle width={panelWidth} onChange={resizePanel} />
          <div className="flex h-11 shrink-0 items-center justify-between border-b border-panel-line px-3">
            <h2 className="font-display text-panel font-semibold text-panel-ink">{panelTitle}</h2>
            {panelBusy && <SpinnerIcon size={12} className="text-panel-ink-3" />}
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3">{simulating ? impact : motor}</div>
        </aside>
      </div>
      {overlays}
    </div>
  );
}
