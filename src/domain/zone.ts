import type { NodeConfiguration, NodeType } from "../types/architecture";

export const ZONES = ["a", "b", "c"] as const;

export type Zone = (typeof ZONES)[number];

// Mirror of NodeRoles.IsYoursToPlace and NodeSettings.Zone in the backend. Lives apart from the
// inspector so the canvas can read zones without opening it. The identity provider left the set on
// 30-08 (QA §10.3, point 14): you pay a SaaS, you do not put it in your aisle of the data centre.
export function isYoursToPlace(type: NodeType): boolean {
  return (
    type !== "Client" && type !== "ExternalService" && type !== "Cdn" && type !== "IdentityProvider"
  );
}

export function nodeZone(type: NodeType, configuration: NodeConfiguration | null | undefined): Zone | null {
  if (!isYoursToPlace(type)) return null;

  const value = configuration?.zone;
  return typeof value === "string" && (ZONES as readonly string[]).includes(value) ? (value as Zone) : null;
}
