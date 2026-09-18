const KEY = "archforge.editor.ui";

export type EditorTheme = "dark" | "light";

/** Una preferencia para las dos superficies: «por defecto» es la web clara y el editor oscuro. */
export type Appearance = "default" | "light" | "dark";

export const PANEL_WIDTH_DEFAULT = 336;
export const PANEL_WIDTH_MIN = 336;
export const PANEL_WIDTH_MAX = 560;

export const PALETTE_WIDTH_DEFAULT = 240;
export const PALETTE_WIDTH_MIN = 200;
export const PALETTE_WIDTH_MAX = 420;

export const INSPECTOR_WIDTH_DEFAULT = 216;
export const INSPECTOR_WIDTH_MIN = 200;
export const INSPECTOR_WIDTH_MAX = 420;

export const SCENARIO_WIDTH_DEFAULT = 264;
export const SCENARIO_WIDTH_MIN = 240;
export const SCENARIO_WIDTH_MAX = 420;

export interface EditorPrefs {
  paletteCollapsed: boolean;
  legendOpen: boolean;
  briefCollapsed: boolean;
  glossesOpen: boolean;
  appearance: Appearance;
  panelWidth: number;
  paletteWidth: number;
  inspectorWidth: number;
  scenarioWidth: number;
}

const DEFAULTS: EditorPrefs = { paletteCollapsed: false, legendOpen: false, briefCollapsed: false, glossesOpen: false, appearance: "default", panelWidth: PANEL_WIDTH_DEFAULT, paletteWidth: PALETTE_WIDTH_DEFAULT, inspectorWidth: INSPECTOR_WIDTH_DEFAULT, scenarioWidth: SCENARIO_WIDTH_DEFAULT };

export const clampPanelWidth = (width: number): number => Math.round(Math.min(PANEL_WIDTH_MAX, Math.max(PANEL_WIDTH_MIN, width)));

export const clampPaletteWidth = (width: number): number => Math.round(Math.min(PALETTE_WIDTH_MAX, Math.max(PALETTE_WIDTH_MIN, width)));

export const clampInspectorWidth = (width: number): number => Math.round(Math.min(INSPECTOR_WIDTH_MAX, Math.max(INSPECTOR_WIDTH_MIN, width)));

export const clampScenarioWidth = (width: number): number => Math.round(Math.min(SCENARIO_WIDTH_MAX, Math.max(SCENARIO_WIDTH_MIN, width)));

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
      appearance: stored.appearance === "light" || stored.appearance === "dark" ? stored.appearance : DEFAULTS.appearance,
      panelWidth: typeof stored.panelWidth === "number" && Number.isFinite(stored.panelWidth) ? clampPanelWidth(stored.panelWidth) : DEFAULTS.panelWidth,
      paletteWidth: typeof stored.paletteWidth === "number" && Number.isFinite(stored.paletteWidth) ? clampPaletteWidth(stored.paletteWidth) : DEFAULTS.paletteWidth,
      inspectorWidth: typeof stored.inspectorWidth === "number" && Number.isFinite(stored.inspectorWidth) ? clampInspectorWidth(stored.inspectorWidth) : DEFAULTS.inspectorWidth,
      scenarioWidth: typeof stored.scenarioWidth === "number" && Number.isFinite(stored.scenarioWidth) ? clampScenarioWidth(stored.scenarioWidth) : DEFAULTS.scenarioWidth,
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
