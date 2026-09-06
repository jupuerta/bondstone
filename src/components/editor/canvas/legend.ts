import type { LocalizedText, NodeType, Severity } from "../../../types/architecture";
import { NODE_GROUPS } from "../palette/nodeMeta";
import { NOT_YOURS_TO_SIZE, SATURATION } from "./loadMeter";

export type LegendMode = "design" | "simulate";

export type LegendMark =
  | { kind: "family"; types: NodeType[] }
  | { kind: "call"; asynchronous: boolean }
  | { kind: "meter" }
  | { kind: "flow" }
  | { kind: "unsized"; types: NodeType[] }
  | { kind: "severities"; order: Severity[] }
  | { kind: "zone"; down: boolean }
  | { kind: "box"; state: BoxState };

export type BoxState = "healthy" | "degraded" | "down" | "contained" | "highlighted";

export interface LegendRow {
  mark: LegendMark;
  label: string;
  note?: string;
  args?: Record<string, string | number | LocalizedText>;
}

export interface LegendSection {
  title: string;
  rows: LegendRow[];
  note?: string;
}

export function unsizedTypes(): NodeType[] {
  return NODE_GROUPS.flatMap((group) => group.types).filter((type) => NOT_YOURS_TO_SIZE.has(type));
}

export function legendSections(mode: LegendMode): LegendSection[] {
  return [
    {
      title: "ui.legend.components",
      rows: NODE_GROUPS.map((group) => ({ mark: { kind: "family", types: group.types } as const, label: group.label })),
      note: "ui.legend.componentsNote",
    },
    {
      title: "ui.legend.calls",
      rows: [
        { mark: { kind: "call", asynchronous: false }, label: "ui.legend.sync", note: "ui.legend.syncNote" },
        { mark: { kind: "call", asynchronous: true }, label: "ui.legend.async", note: "ui.legend.asyncNote" },
      ],
    },
    mode === "simulate" ? SIMULATE_SECTION : designSection(),
  ];
}

function designSection(): LegendSection {
  return {
    title: "ui.legend.design",
    rows: [
      { mark: { kind: "meter" }, label: "ui.legend.load", note: "editor.legend.loadNote", args: { value: SATURATION } },
      { mark: { kind: "unsized", types: unsizedTypes() }, label: "ui.legend.unsized", note: "ui.legend.unsizedNote" },
      { mark: { kind: "flow" }, label: "ui.legend.flow", note: "ui.legend.flowNote" },
      { mark: { kind: "severities", order: ["Critical", "Warning"] }, label: "ui.legend.badges", note: "editor.legend.badgesNote", args: { toggle: { key: "ui.editor.badges" } } },
      { mark: { kind: "zone", down: false }, label: "ui.legend.zone", note: "ui.legend.zoneNote" },
      { mark: { kind: "box", state: "highlighted" }, label: "ui.legend.highlighted", note: "editor.legend.highlightedNote" },
    ],
  };
}

const SIMULATE_SECTION: LegendSection = {
  title: "ui.legend.simulate",
  rows: [
    { mark: { kind: "box", state: "healthy" }, label: "ui.legend.healthy", note: "editor.legend.healthyNote" },
    { mark: { kind: "box", state: "degraded" }, label: "ui.legend.degraded", note: "editor.legend.degradedNote" },
    { mark: { kind: "box", state: "down" }, label: "ui.legend.down", note: "editor.legend.downNote" },
    { mark: { kind: "box", state: "contained" }, label: "ui.legend.contained", note: "editor.legend.containedNote" },
    { mark: { kind: "zone", down: true }, label: "ui.legend.zoneDown", note: "editor.legend.zoneDownNote" },
  ],
};
