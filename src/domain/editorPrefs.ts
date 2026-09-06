const KEY = "archforge.editor.ui";

export interface EditorPrefs {
  paletteCollapsed: boolean;
}

const DEFAULTS: EditorPrefs = { paletteCollapsed: false };

export function editorPrefs(): EditorPrefs {
  try {
    const raw = window.localStorage.getItem(KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (parsed === null || typeof parsed !== "object") return DEFAULTS;
    const stored = parsed as Partial<EditorPrefs>;
    return { paletteCollapsed: typeof stored.paletteCollapsed === "boolean" ? stored.paletteCollapsed : DEFAULTS.paletteCollapsed };
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
