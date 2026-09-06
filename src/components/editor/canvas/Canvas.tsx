import { useCallback, useEffect, useMemo, useRef, type DragEvent, type ReactNode } from "react";
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  ViewportPortal,
  useEdgesState,
  useNodesState,
  useOnSelectionChange,
  useReactFlow,
  type Edge,
  type Node,
} from "@xyflow/react";
import { useT } from "../../../i18n";
import type { ArchitectureDto, ConnectionLoad, ElementState, NodeType, PositionDto } from "../../../types/architecture";
import { tintOf } from "../../engine/tints";
import { DND_NODE_TYPE_FORMAT } from "../palette/nodeMeta";
import { displayEdges, displayNodes, statesOf } from "./display";
import { EditorEdge } from "./Edge";
import { NODE_H, NODE_W } from "./marks";
import { EditorNode } from "./Node";
import { NODE_TYPES, type ArchNodeData, type ConnectionData, type Selection } from "./types";
import { ZoneBand } from "./ZoneBand";
import { bandDown, zoneBands } from "./zoneBands";

const NODE_COMPONENTS = Object.fromEntries(NODE_TYPES.map((type) => [type, EditorNode]));
const EDGE_COMPONENTS = { default: EditorEdge };
const DELETE_KEYS = ["Delete", "Backspace"];

export interface CanvasEdits {
  onAddNode: (type: NodeType, position: PositionDto) => void;
  onConnect: (source: string, target: string) => void;
  onMove: (positions: Map<string, PositionDto>) => void;
  onDelete: (nodeIds: Set<string>, edgeIds: Set<string>) => void;
}

interface CanvasProps {
  architecture: ArchitectureDto;
  states: ElementState[] | null;
  faultIds: ReadonlySet<string>;
  callLoad?: ConnectionLoad[] | null;
  selection: Selection | null;
  onSelect: (selection: Selection | null) => void;
  narrow: boolean;
  edits?: CanvasEdits;
  empty?: ReactNode;
}

export function Canvas(props: CanvasProps) {
  return (
    <ReactFlowProvider>
      <CanvasInner {...props} />
    </ReactFlowProvider>
  );
}

function CanvasInner({ architecture, states, faultIds, callLoad, selection, onSelect, narrow, edits, empty }: CanvasProps) {
  const t = useT();
  const simulated = states !== null;
  const editable = edits !== undefined;
  const nodeStates = useMemo(() => statesOf(states, "Node"), [states]);
  const connectionStates = useMemo(() => statesOf(states, "Connection"), [states]);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node<ArchNodeData>>(displayNodes(architecture, nodeStates, faultIds, simulated, selection));
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge<ConnectionData>>(displayEdges(architecture, nodeStates, connectionStates, callLoad, selection));
  const bands = useMemo(
    () => zoneBands(nodes.map((node) => ({ id: node.id, type: node.type as NodeType, position: node.position, width: node.measured?.width, height: node.measured?.height, configuration: node.data.configuration }))),
    [nodes],
  );

  useEffect(() => {
    setNodes((current) => {
      const kept = new Set(current.filter((node) => node.selected).map((node) => node.id));
      return displayNodes(architecture, nodeStates, faultIds, simulated, null).map((node) => ({ ...node, selected: kept.has(node.id) }));
    });
  }, [architecture, nodeStates, faultIds, simulated, setNodes]);

  useEffect(() => {
    setEdges((current) => {
      const kept = new Set(current.filter((edge) => edge.selected).map((edge) => edge.id));
      return displayEdges(architecture, nodeStates, connectionStates, callLoad, null).map((edge) => ({ ...edge, selected: kept.has(edge.id) }));
    });
  }, [architecture, nodeStates, connectionStates, callLoad, setEdges]);

  useEffect(() => {
    if (selection !== null) return;
    setNodes((current) => (current.some((node) => node.selected) ? current.map((node) => ({ ...node, selected: false })) : current));
    setEdges((current) => (current.some((edge) => edge.selected) ? current.map((edge) => ({ ...edge, selected: false })) : current));
  }, [selection, setNodes, setEdges]);

  const onSelectionChange = useCallback(
    ({ nodes: picked, edges: pickedEdges }: { nodes: Node[]; edges: Edge[] }) => {
      const next: Selection | null = picked[0] ? { kind: "node", id: picked[0].id } : pickedEdges[0] ? { kind: "edge", id: pickedEdges[0].id } : null;
      onSelect(next);
    },
    [onSelect],
  );
  useOnSelectionChange({ onChange: onSelectionChange });

  const { fitView, getZoom, screenToFlowPosition } = useReactFlow();
  const frame = useRef<HTMLElement>(null);
  const lastWidth = useRef<number | null>(null);
  const chosen = useRef<Selection | null>(selection);
  chosen.current = selection;
  const padding = narrow ? 0.1 : 0.2;
  const still = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  useEffect(() => {
    const element = frame.current;
    if (!element) return;

    const observer = new ResizeObserver(([entry]) => {
      const width = entry.contentRect.width;
      const previous = lastWidth.current;
      lastWidth.current = width;
      if (previous === null) return;

      const duration = still() ? 0 : 150;

      if (Math.abs(width - previous) > 1) {
        void fitView({ padding, maxZoom: 1, duration });
        return;
      }

      const picked = chosen.current;
      if (picked?.kind === "node") void fitView({ nodes: [{ id: picked.id }], maxZoom: getZoom(), padding: 0.6, duration });
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, [fitView, getZoom, padding]);

  const drawn = architecture.nodes.length > 0;
  const wasDrawn = useRef(drawn);
  useEffect(() => {
    if (drawn && !wasDrawn.current) {
      const frameId = requestAnimationFrame(() => void fitView({ padding, maxZoom: 1, duration: still() ? 0 : 240 }));
      wasDrawn.current = true;
      return () => cancelAnimationFrame(frameId);
    }
    wasDrawn.current = drawn;
  }, [drawn, fitView, padding]);

  const onDragOver = useCallback(
    (event: DragEvent) => {
      if (!editable) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
    },
    [editable],
  );

  const onDrop = useCallback(
    (event: DragEvent) => {
      if (!edits) return;
      event.preventDefault();
      const type = event.dataTransfer.getData(DND_NODE_TYPE_FORMAT) as NodeType | "";
      if (!type) return;
      const point = screenToFlowPosition({ x: event.clientX, y: event.clientY });
      edits.onAddNode(type, { x: Math.round(point.x - NODE_W / 2), y: Math.round(point.y - NODE_H / 2) });
    },
    [edits, screenToFlowPosition],
  );

  const ariaLabels = useMemo(
    () => ({
      "node.a11yDescription.default": t("editor.a11y.node"),
      "node.a11yDescription.keyboardDisabled": t("editor.a11y.node"),
      "edge.a11yDescription.default": t("editor.a11y.edge"),
      "controls.ariaLabel": t("editor.a11y.controls"),
      "controls.zoomIn.ariaLabel": t("editor.a11y.zoomIn"),
      "controls.zoomOut.ariaLabel": t("editor.a11y.zoomOut"),
      "controls.fitView.ariaLabel": t("editor.a11y.fitView"),
      "minimap.ariaLabel": t("editor.a11y.minimap"),
    }),
    [t],
  );

  return (
    <section
      ref={frame}
      aria-label={t("editor.a11y.canvas", { name: architecture.name })}
      className={`relative min-h-0 min-w-0 flex-1 bg-canvas ${editable ? "af-editable" : ""}`}
      onDragOver={onDragOver}
      onDrop={onDrop}
    >
      <ReactFlow<Node<ArchNodeData>, Edge<ConnectionData>>
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_COMPONENTS}
        edgeTypes={EDGE_COMPONENTS}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={edits ? (connection) => edits.onConnect(connection.source, connection.target) : undefined}
        isValidConnection={(connection) => connection.source !== connection.target}
        onNodeDragStop={edits ? (_, __, dragged) => edits.onMove(new Map(dragged.map((node) => [node.id, node.position]))) : undefined}
        onDelete={edits ? ({ nodes: gone, edges: goneEdges }) => edits.onDelete(new Set(gone.map((node) => node.id)), new Set(goneEdges.map((edge) => edge.id))) : undefined}
        nodesDraggable={editable}
        nodesConnectable={editable}
        edgesReconnectable={false}
        elementsSelectable
        deleteKeyCode={editable ? DELETE_KEYS : null}
        selectionKeyCode={null}
        multiSelectionKeyCode={null}
        zoomOnDoubleClick={false}
        minZoom={0.25}
        maxZoom={2}
        fitView
        fitViewOptions={{ padding, maxZoom: 1 }}
        colorMode="dark"
        ariaLabelConfig={ariaLabels}
      >
        <Background variant={BackgroundVariant.Dots} gap={24} size={1} />
        <ViewportPortal>
          {bands.map((band) => (
            <ZoneBand key={band.zone} band={band} down={bandDown(band, nodeStates)} />
          ))}
        </ViewportPortal>
        {!narrow && <Controls showInteractive={false} position="bottom-left" />}
        {!narrow && (
          <MiniMap
            pannable
            zoomable
            nodeColor={(node) => tintOf(node.type as ArchitectureDto["nodes"][number]["type"])}
            nodeStrokeWidth={0}
            position="bottom-right"
            style={{ width: 150, height: 96 }}
          />
        )}
      </ReactFlow>

      {!drawn && (
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-5 overflow-y-auto p-6">
          <p className="rounded-card border border-panel-line bg-panel-2 px-4 py-2 text-small text-panel-ink-2">{t(editable ? "editor.canvas.empty" : "editor.canvas.emptyReadOnly")}</p>
          {empty}
        </div>
      )}
    </section>
  );
}
