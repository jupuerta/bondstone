import type { ElementState, NodeConfiguration, NodeType } from "../../../types/architecture";
import { ZONES, nodeZone, type Zone } from "../../../domain/zone";
import { NODE_H, NODE_W } from "./marks";

export const BAND_PAD = 24;

export interface BandNode {
  id: string;
  type: NodeType;
  position: { x: number; y: number };
  width?: number | null;
  height?: number | null;
  configuration?: NodeConfiguration | null;
}

export interface ZoneBand {
  zone: Zone;
  x: number;
  y: number;
  w: number;
  h: number;
  nodeIds: string[];
}

export function zoneBands(nodes: readonly BandNode[]): ZoneBand[] {
  const bands: ZoneBand[] = [];

  for (const zone of ZONES) {
    const members = nodes.filter((node) => nodeZone(node.type, node.configuration) === zone);
    if (members.length === 0) continue;

    const left = Math.min(...members.map((node) => node.position.x));
    const top = Math.min(...members.map((node) => node.position.y));
    const right = Math.max(...members.map((node) => node.position.x + (node.width ?? NODE_W)));
    const bottom = Math.max(...members.map((node) => node.position.y + (node.height ?? NODE_H)));

    bands.push({
      zone,
      x: left - BAND_PAD,
      y: top - BAND_PAD,
      w: right - left + BAND_PAD * 2,
      h: bottom - top + BAND_PAD * 2,
      nodeIds: members.map((node) => node.id),
    });
  }

  return bands.sort((one, other) => other.w * other.h - one.w * one.h);
}

export function bandDown(band: ZoneBand, states: ReadonlyMap<string, ElementState>): boolean {
  return band.nodeIds.every((id) => states.get(id)?.state === "Down");
}
