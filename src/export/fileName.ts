import type { ArchitectureDto } from "../types/architecture";

export function fileSlug(name: string): string {
  return (
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-|-$/g, "") || "architecture"
  );
}

export function exportFileName(architecture: ArchitectureDto, extension: string): string {
  return `${fileSlug(architecture.name)}-v${architecture.version}.${extension}`;
}
