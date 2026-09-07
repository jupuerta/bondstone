const KEY = "archforge.editor.ui";

export type EditorTheme = "dark" | "light";

export const PANEL_WIDTH_DEFAULT = 336;
export const PANEL_WIDTH_MIN = 336;
export const PANEL_WIDTH_MAX = 560;

export interface EditorPrefs {
  paletteCollapsed: boolean;
  legendOpen: boolean;
  briefCollapsed: boolean;
  glossesOpen: boolean;
  theme: EditorTheme;
  panelWidth: number;
}

const DEFAULTS: EditorPrefs = { paletteCollapsed: false, legendOpen: false, briefCollapsed: false, glossesOpen: false, theme: "dark", panelWidth: PANEL_WIDTH_DEFAULT };

export const clampPanelWidth = (width: number): number => Math.round(Math.min(PANEL_WIDTH_MAX, Math.max(PANEL_WIDTH_MIN, width)));

export function editorPrefs(): EditorPrefs {
  try {
    const raw = window.localStorage.getItem(KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (parsed === null || typeof parsed !== "object") return DEFAULTS;
    const stored = parsed as Partial<EditorPrefs>;
    return {
      paletteCollapsed: typeof stored.paletteCollapsed === "boolean" ? stored.paletteCollapsed : DEFAULTS.paletteCollapsed,
      legendOpen: typeof stored.legendOpen === "boolean" ? stored.legendOpen : DEFAULTS.legendOpen,
      briefCollapsed: typeof stored.briefCollapsed === "boolean" ? stored.briefCollapsed : DEFAULTS.briefCollapsed,
      glossesOpen: typeof stored.glossesOpen === "boolean" ? stored.glossesOpen : DEFAULTS.glossesOpen,
      theme: stored.theme === "light" ? "light" : DEFAULTS.theme,
      panelWidth: typeof stored.panelWidth === "number" && Number.isFinite(stored.panelWidth) ? clampPanelWidth(stored.panelWidth) : DEFAULTS.panelWidth,
    };
  } catch {
    return DEFAULTS;
  }
}

export function rememberEditorPrefs(patch: Partial<EditorPrefs>): void {
  try {
    window.localStorage.setItem(KEY, JSON.stringify({ ...editorPrefs(), ...patch }));
  } catch {
    return;
  }
}
