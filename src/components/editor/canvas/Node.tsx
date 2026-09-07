import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { useT } from "../../../i18n";
import type { NodeType } from "../../../types/architecture";
import { TypeIcon } from "../palette/TypeIcons";
import { FindingBadge } from "./FindingBadge";
import { LoadMeter } from "./LoadMeter";
import { loadMeter } from "./loadMeter";
import { nodeHeight, statusTone } from "./display";
import { NODE_W } from "./marks";
import type { ArchNodeData } from "./types";

type EditorNodeProps = NodeProps<Node<ArchNodeData>>;

export function EditorNode({ type, data }: EditorNodeProps) {
  const t = useT();
  const nodeType = type as NodeType;
  const status = data.status ?? null;
  const tone = statusTone(status);
  const simulated = status !== null;
  const plate = `color-mix(in oklab, var(--af-t-${nodeType}) 9%, var(--af-panel-2))`;
  const ring = status?.mechanism ? "af-node-contained" : data.highlighted ? "af-node-highlighted" : "";
  const load = data.load ?? null;
  const metered = load !== null && loadMeter(nodeType, load) !== null;
  const scale = data.loadScale ?? 1;
  const reading = load ? { loadRps: load.rps, capacityRps: load.capacityRps, utilization: load.utilization } : null;

  return (
    <div className="relative" style={{ width: NODE_W, height: nodeHeight(simulated) }} title={metered && reading ? (scale > 1 ? t("ui.node.loadAt", { scale, ...reading }) : t("ui.node.load", reading)) : undefined}>
      <div
        className={`af-node relative h-full overflow-hidden rounded-node border ${ring}`}
        style={{
          "--af-node-plate": tone.color ? `color-mix(in oklab, ${tone.color} 12%, ${plate})` : plate,
          borderColor: tone.color ?? "var(--af-panel-line)",
        } as React.CSSProperties}
      >
        <Handle type="target" position={Position.Top} className="af-port" />
        <div className="h-[3px]" style={{ background: `var(--af-t-${nodeType})` }} />
        <div className={`px-2.5 pt-[6px] ${simulated ? "pb-1.5" : "pb-2"}`}>
          <div className="flex items-center gap-1.5">
            <TypeIcon type={nodeType} size={13} className="shrink-0" />
            <span className="truncate font-mono text-[10px] uppercase leading-[1.2] tracking-[0.09em] text-panel-ink-3">{t(`ui.node.${nodeType}`)}</span>
          </div>
          <div className="mt-px truncate text-[13px] leading-[1.25] font-semibold text-panel-ink">{data.name}</div>
          {tone.state && (
            <div className="mt-[3px] flex items-center gap-[5px] font-mono text-[10px] leading-[1.2] tracking-[0.04em]" style={{ color: tone.color ?? "var(--af-panel-ink-3)" }}>
              <span
                aria-hidden
                className={`size-2 shrink-0 rounded-full ${tone.filled ? "" : "border-2 border-current"}`}
                style={{ background: tone.filled ? "currentColor" : "transparent" }}
              />
              <span className={`truncate ${tone.state.upper ? "uppercase" : ""}`}>{t(tone.state.key)}</span>
            </div>
          )}
          {tone.detail.length > 0 && (
            <div className="mt-[3px] truncate font-mono text-[10px] leading-[1.2] text-panel-ink-3">{tone.detail.map((key) => t(key)).join(" · ")}</div>
          )}
        </div>
        {!status && <LoadMeter type={nodeType} load={load} />}
        <Handle type="source" position={Position.Bottom} className="af-port" />
      </div>
      {data.badge && <FindingBadge severity={data.badge} />}
    </div>
  );
}
