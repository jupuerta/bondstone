import { useCallback, useEffect, useMemo, useRef } from "react";
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
import type { ArchitectureDto, ConnectionLoad, ElementState } from "../../../types/architecture";
import { tintOf } from "../../engine/tints";
import { displayEdges, displayNodes, statesOf } from "./display";
import { EditorEdge } from "./Edge";
import { EditorNode } from "./Node";
import { NODE_TYPES, type ArchNodeData, type ConnectionData, type Selection } from "./types";
import { ZoneBand } from "./ZoneBand";
import { bandDown, zoneBands } from "./zoneBands";

const NODE_COMPONENTS = Object.fromEntries(NODE_TYPES.map((type) => [type, EditorNode]));
const EDGE_COMPONENTS = { default: EditorEdge };

interface CanvasProps {
  architecture: ArchitectureDto;
  states: ElementState[] | null;
  faultIds: ReadonlySet<string>;
  callLoad?: ConnectionLoad[] | null;
  selection: Selection | null;
  onSelect: (selection: Selection | null) => void;
  narrow: boolean;
}

export function Canvas(props: CanvasProps) {
  return (
    <ReactFlowProvider>
      <CanvasInner {...props} />
    </ReactFlowProvider>
  );
}

function CanvasInner({ architecture, states, faultIds, callLoad, selection, onSelect, narrow }: CanvasProps) {
  const t = useT();
  const simulated = states !== null;
  const nodeStates = useMemo(() => statesOf(states, "Node"), [states]);
  const connectionStates = useMemo(() => statesOf(states, "Connection"), [states]);
  const bands = useMemo(() => zoneBands(architecture.nodes), [architecture]);
  const [nodes, setNodes, onNodesChange] = useNodesState<Node<ArchNodeData>>(displayNodes(architecture, nodeStates, faultIds, simulated, selection));
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge<ConnectionData>>(displayEdges(architecture, nodeStates, connectionStates, callLoad, selection));

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

  const { fitView } = useReactFlow();
  const frame = useRef<HTMLElement>(null);
  const padding = narrow ? 0.1 : 0.2;
  useEffect(() => {
    const element = frame.current;
    if (!element) return;
    let first = true;
    const observer = new ResizeObserver(() => {
      if (first) {
        first = false;
        return;
      }
      const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      void fitView({ padding, maxZoom: 1, duration: still ? 0 : 150 });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, [fitView, padding]);

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
    <section ref={frame} aria-label={t("editor.a11y.canvas", { name: architecture.name })} className="relative min-h-0 min-w-0 flex-1 bg-canvas">
      <ReactFlow<Node<ArchNodeData>, Edge<ConnectionData>>
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_COMPONENTS}
        edgeTypes={EDGE_COMPONENTS}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        nodesDraggable={false}
        nodesConnectable={false}
        edgesReconnectable={false}
        elementsSelectable
        deleteKeyCode={null}
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

      {architecture.nodes.length === 0 && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center p-6">
          <p className="rounded-card border border-panel-line bg-panel-2 px-4 py-2 text-small text-panel-ink-2">{t("editor.canvas.emptyReadOnly")}</p>
        </div>
      )}
    </section>
  );
}
