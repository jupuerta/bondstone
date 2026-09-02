import type { NodeType } from "../../types/architecture";

/** El tinte de cada tipo vive en index.css como --af-t-<Tipo>. */
export function tintOf(type: NodeType): string {
  return `var(--af-t-${type})`;
}
