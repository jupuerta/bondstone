import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getChallenge } from "../../api/challengeClient";
import { editorPrefs, rememberEditorPrefs } from "../../domain/editorPrefs";
import { rememberWork, scoreWork, touchWork } from "../../domain/savedWork";
import { useAutosave } from "../../hooks/useAutosave";
import { useLiveAnalysis } from "../../hooks/useLiveAnalysis";
import { useNarrowViewport } from "../../hooks/useMediaQuery";
import { usePageTitle } from "../../hooks/usePageTitle";
import { useT } from "../../i18n";
import type { ArchitectureDto, ChallengeDetail, NodeType, PositionDto, ResilienceConfiguration, UpdateArchitectureResult } from "../../types/architecture";
import { Button } from "../ui/Button";
import { Skeleton } from "../ui/Skeleton";
import { Canvas, type CanvasEdits } from "./canvas/Canvas";
import { addNode, connect, connectedEdges, entryPointOf, importGraph, isTypingTarget, moveNodes, patchEdge, patchNode, removeEdges, removeNodes, setNodeField } from "./canvas/graphEdits";
import { EMPTY_HISTORY, record, redo, undo, type Graph, type GraphHistory } from "./canvas/graphHistory";
import { flowToArchitecture, flowToUpdateRequest } from "./canvas/mapping";
import type { Selection } from "./canvas/types";
import { EdgeInspector } from "./inspector/EdgeInspector";
import { NodeInspector } from "./inspector/NodeInspector";
import { ConflictDialog } from "./overlays/ConflictDialog";
import { Palette } from "./palette/Palette";
import { elementOf, endsOf, titleOf } from "./selection";
import { BottomDrawer } from "./shell/BottomDrawer";
import { EditorBar, type BarChip } from "./shell/EditorBar";
import { MotorSummary } from "./shell/MotorSummary";
import { NarrowMenu } from "./shell/NarrowMenu";
import { SaveState } from "./shell/SaveState";
import { useShareLink } from "./shell/useShareLink";
import { Starters } from "./starters/Starters";
import { starterToDto, type StarterArchitecture } from "./starters/starterArchitectures";

interface EditorProps {
  architecture: ArchitectureDto;
  editToken: string;
  search: string;
  onReload: () => void;
}

const EMPTY_FAULTS: ReadonlySet<string> = new Set();

export function Editor({ architecture, editToken, search, onReload }: EditorProps) {
  const t = useT();
  const narrow = useNarrowViewport();
  const editable = !narrow;

  const [graph, setGraph] = useState<Graph>(() => importGraph(architecture.nodes, architecture.connections));
  const [name, setName] = useState(architecture.name);
  const [version, setVersion] = useState(architecture.version);
  const [selection, setSelection] = useState<Selection | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [paletteCollapsed, setPaletteCollapsed] = useState(() => editorPrefs().paletteCollapsed);
  const [conflict, setConflict] = useState(false);
  const [savedRevision, setSavedRevision] = useState(0);
  const challengeSlug = useMemo(() => new URLSearchParams(search).get("challenge"), [search]);
  const [challenge, setChallenge] = useState<ChallengeDetail | null>(null);
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

  useEffect(() => {
    rememberWork({ editToken, shareToken: architecture.shareToken, name: architecture.name, challengeSlug });
  }, [editToken, architecture.shareToken, architecture.name, challengeSlug]);

  useEffect(() => {
    setChallenge(null);
    if (!challengeSlug) return;
    let cancelled = false;
    getChallenge(challengeSlug)
      .then((detail) => {
        if (!cancelled) setChallenge(detail);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [challengeSlug]);

  const getSnapshot = useCallback(() => flowToUpdateRequest(nameRef.current, versionRef.current, graphRef.current.nodes, graphRef.current.edges), []);
  const onSaved = useCallback(
    (result: UpdateArchitectureResult) => {
      setVersion(result.version);
      setSavedRevision((current) => current + 1);
      touchWork(editToken, { name: nameRef.current });
    },
    [editToken],
  );
  const onConflict = useCallback(() => setConflict(true), []);
  const autosave = useAutosave({ editToken, enabled: editable, getSnapshot, onSaved, onConflict });

  const lastScheduled = useRef({ graph, name });
  const { scheduleSave } = autosave;
  useEffect(() => {
    if (lastScheduled.current.graph === graph && lastScheduled.current.name === name) return;
    lastScheduled.current = { graph, name };
    scheduleSave();
  }, [graph, name, scheduleSave]);

  const analysis = useLiveAnalysis({ shareToken: architecture.shareToken, savedRevision });
  useEffect(() => {
    if (analysis.result) scoreWork(editToken, analysis.result.score);
  }, [analysis.result, editToken]);

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

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        if (!isTypingTarget(event.target)) setSelection(null);
        return;
      }
      if (!editable || !(event.ctrlKey || event.metaKey) || event.altKey) return;
      const key = event.key.toLowerCase();
      if (key !== "z" && key !== "y") return;
      if (isTypingTarget(event.target)) return;
      event.preventDefault();
      step(key === "y" || event.shiftKey ? redo : undo);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [editable, step]);

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
  const faultIds = EMPTY_FAULTS;
  const chips: BarChip[] = challenge ? [{ key: "challenge", label: `${t("ui.challenge.title")} · ${t(challenge.title)}` }] : [];
  const saveState = <SaveState status={autosave.status} version={version} error={autosave.error} onRetry={autosave.retryNow} narrow={narrow} />;

  const inspector = selected && (
    <div className="flex flex-col gap-3">
      {selected.kind === "node" ? (
        <NodeInspector
          node={selected.node}
          isEntryPoint={entryPointOf(graph, selected.node.id)}
          heading={narrow}
          edits={
            editable
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
          edits={
            editable
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

  const motor = analysis.result ? (
    <MotorSummary analysis={analysis.result} shareToken={architecture.shareToken} faultIds={faultIds} />
  ) : analysis.status === "error" ? (
    <p className="text-small text-hot-panel">{analysis.error}</p>
  ) : (
    <div className="flex flex-col gap-3" aria-busy>
      <Skeleton height={96} dark />
      <Skeleton width="70%" dark />
      <Skeleton width="50%" dark />
    </div>
  );

  const shell = (
    <EditorBar
      name={name}
      onRename={editable ? setName : undefined}
      shareToken={architecture.shareToken}
      chips={chips}
      narrow={narrow}
      status={saveState}
      onShare={copy}
      shared={copied}
      onOpenMenu={() => setMenuOpen(true)}
      menuOpen={menuOpen}
    />
  );

  const canvas = (
    <Canvas
      architecture={dto}
      states={null}
      faultIds={faultIds}
      callLoad={analysis.result?.callLoad ?? null}
      selection={selection}
      onSelect={select}
      narrow={narrow}
      edits={editable ? edits : undefined}
      empty={
        editable ? (
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

  if (narrow) {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        {shell}
        <p className="shrink-0 border-b border-panel-line bg-panel-3 px-3 py-2 text-meta text-panel-ink-2">{t("web.engine.narrow")}</p>
        {canvas}
        <BottomDrawer
          title={selected ? titleOf(selected, dto, t) : `${t("ui.analysis.title")} · ${analysis.result?.score ?? "—"}`}
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
        {menuOpen && <NarrowMenu shareToken={architecture.shareToken} onClose={closeMenu} onShare={copy} shared={copied} />}
        {conflict && <ConflictDialog onReload={onReload} />}
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {shell}
      <div className="flex min-h-0 flex-1">
        <aside className={`flex shrink-0 border-r border-panel-line bg-panel ${inspector || paletteCollapsed ? "w-auto" : "w-[240px]"}`}>
          {inspector ? (
            <>
              <Palette collapsed onToggle={togglePalette} />
              <div className="flex w-[216px] flex-col">
                <div className="flex h-11 shrink-0 items-center justify-between border-b border-panel-line px-3">
                  <span className="eyebrow text-panel-ink-3">{selected?.kind === "node" ? t("editor.inspector.component") : t("ui.inspector.call")}</span>
                  <button type="button" onClick={() => setSelection(null)} className="rounded-pill px-2 py-1 text-meta text-panel-ink-2 hover:text-panel-ink">
                    {t("ui.close")}
                  </button>
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto p-3">{inspector}</div>
              </div>
            </>
          ) : (
            <Palette collapsed={paletteCollapsed} onToggle={togglePalette} />
          )}
        </aside>
        {canvas}
        <aside className="flex w-[336px] shrink-0 flex-col border-l border-panel-line bg-panel">
          <div className="flex h-11 shrink-0 items-center border-b border-panel-line px-3">
            <h2 className="font-display text-panel font-semibold text-panel-ink">{t("ui.analysis.title")}</h2>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-3">{motor}</div>
        </aside>
      </div>
      {conflict && <ConflictDialog onReload={onReload} />}
    </div>
  );
}
