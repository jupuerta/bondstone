import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { TypeIcon } from "../palette/TypeIcons";
import { NODE_H, NODE_W } from "./marks";
import type { ArchNodeData, NodeStatus } from "./types";

type EditorNodeProps = NodeProps<Node<ArchNodeData>>;

export function EditorNode({ type, data }: EditorNodeProps) {
  const t = useT();
  const nodeType = type as NodeType;
  const status = data.status ?? null;
  const tone = toneOf(status);
  const plate = `color-mix(in oklab, var(--af-t-${nodeType}) 9%, var(--af-panel-2))`;
  const ring = status?.mechanism ? "af-node-contained" : "";

  return (
    <div
      className={`af-node relative overflow-hidden rounded-node border ${ring}`}
      style={{
        width: NODE_W,
        height: NODE_H,
        "--af-node-plate": tone.color ? `color-mix(in oklab, ${tone.color} 12%, ${plate})` : plate,
        borderColor: tone.color ?? "var(--af-panel-line)",
      } as React.CSSProperties}
    >
      <Handle type="target" position={Position.Top} className="af-port" />
      <div className="h-[3px]" style={{ background: `var(--af-t-${nodeType})` }} />
      <div className="px-2.5 pt-[6px] pb-2">
        <div className="flex items-center gap-1.5">
          <TypeIcon type={nodeType} size={13} className="shrink-0" />
          <span className="truncate font-mono text-[10px] uppercase leading-[1.2] tracking-[0.09em] text-panel-ink-3">{t(`ui.node.${nodeType}`)}</span>
        </div>
        <div className="mt-px truncate text-[13px] leading-[1.25] font-semibold text-panel-ink">{data.name}</div>
        {status && tone.words.length > 0 && (
          <div className="mt-[3px] flex items-center gap-[5px] font-mono text-[10px] leading-[1.2] tracking-[0.04em]" style={{ color: tone.color ?? "var(--af-panel-ink-3)" }}>
            <span
              aria-hidden
              className={`size-2 shrink-0 rounded-full ${tone.filled ? "" : "border-2 border-current"}`}
              style={{ background: tone.filled ? "currentColor" : "transparent" }}
            />
            <span className="truncate">
              {tone.words.map((word, index) => (
                <span key={word.key} className={word.upper ? "uppercase" : ""}>
                  {index > 0 && " · "}
                  {t(word.key)}
                </span>
              ))}
            </span>
          </div>
        )}
      </div>
      <Handle type="source" position={Position.Bottom} className="af-port" />
    </div>
  );
}

interface Tone {
  color: string | null;
  filled: boolean;
  words: { key: string; upper?: boolean }[];
}

function toneOf(status: NodeStatus | null): Tone {
  if (!status) return { color: null, filled: false, words: [] };
  const mechanism = status.mechanism ? [{ key: `ui.mechanism.${status.mechanism}` }] : [];

  if (status.health === "Down") {
    const cause = status.injected ? [{ key: "web.engine.injected" }] : status.dragged ? [{ key: "web.engine.dragged" }] : [];
    return { color: "var(--af-hot-panel)", filled: true, words: [{ key: "web.engine.state.Down", upper: true }, ...cause, ...mechanism] };
  }
  if (status.health === "Degraded") {
    return { color: "var(--af-warm-panel)", filled: false, words: [{ key: "web.engine.state.Degraded" }, ...mechanism] };
  }
  if (status.health === null) {
    return { color: null, filled: false, words: [{ key: "web.engine.state.none" }] };
  }
  return { color: status.mechanism ? "var(--af-warm-panel)" : null, filled: false, words: mechanism };
}
