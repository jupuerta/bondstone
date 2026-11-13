export const DOCS_WINDOW = "bondstone-docs";

export function docsLinks(shareToken: string): { key: string; href: string }[] {
  return [
    { key: "ui.editor.model", href: "/model" },
    { key: "ui.editor.rules", href: `/rules/${encodeURIComponent(shareToken)}` },
    { key: "ui.editor.mechanisms", href: "/mechanisms" },
  ];
}
