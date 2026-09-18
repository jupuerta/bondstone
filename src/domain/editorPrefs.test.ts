import { beforeEach, describe, expect, it } from "vitest";
import { editorPrefs, rememberEditorPrefs } from "./editorPrefs";

const DEFAULTS = { paletteCollapsed: false, legendOpen: false, briefCollapsed: false, glossesOpen: false, appearance: "default", panelWidth: 336, paletteWidth: 240, inspectorWidth: 216, scenarioWidth: 264 };

describe("what the editor remembers about its own layout", () => {
  beforeEach(() => window.localStorage.clear());

  it("starts with the palette open, the legend closed, the brief unfolded, the appearance untouched, and the panel at 336", () => {
    expect(editorPrefs()).toEqual(DEFAULTS);
  });

  it("keeps the palette folded across reloads, in one key", () => {
    rememberEditorPrefs({ paletteCollapsed: true });
    rememberEditorPrefs({ legendOpen: true });
    rememberEditorPrefs({ briefCollapsed: true, glossesOpen: false });
    expect(editorPrefs()).toEqual({ ...DEFAULTS, paletteCollapsed: true, legendOpen: true, briefCollapsed: true });
    expect(Object.keys(window.localStorage)).toEqual(["archforge.editor.ui"]);
  });

  it("remembers light and dark, and anything else is the default appearance", () => {
    rememberEditorPrefs({ appearance: "light" });
    expect(editorPrefs().appearance).toBe("light");
    rememberEditorPrefs({ appearance: "dark" });
    expect(editorPrefs().appearance).toBe("dark");
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ appearance: "system" }));
    expect(editorPrefs().appearance).toBe("default");
  });

  it("remembers the panel width inside its bounds", () => {
    rememberEditorPrefs({ panelWidth: 420 });
    expect(editorPrefs().panelWidth).toBe(420);
    rememberEditorPrefs({ panelWidth: 900 });
    expect(editorPrefs().panelWidth).toBe(560);
    rememberEditorPrefs({ panelWidth: 100 });
    expect(editorPrefs().panelWidth).toBe(336);
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ panelWidth: "wide" }));
    expect(editorPrefs().panelWidth).toBe(336);
  });

  it("remembers the inspector and the scenario widths inside their bounds", () => {
    rememberEditorPrefs({ inspectorWidth: 300, scenarioWidth: 300 });
    expect(editorPrefs().inspectorWidth).toBe(300);
    expect(editorPrefs().scenarioWidth).toBe(300);
    rememberEditorPrefs({ inspectorWidth: 900, scenarioWidth: 900 });
    expect(editorPrefs().inspectorWidth).toBe(420);
    expect(editorPrefs().scenarioWidth).toBe(420);
    rememberEditorPrefs({ inspectorWidth: 40, scenarioWidth: 40 });
    expect(editorPrefs().inspectorWidth).toBe(200);
    expect(editorPrefs().scenarioWidth).toBe(240);
  });

  it("ignores junk", () => {
    window.localStorage.setItem("archforge.editor.ui", "{nope");
    expect(editorPrefs()).toEqual(DEFAULTS);
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ paletteCollapsed: "yes", briefCollapsed: 1 }));
    expect(editorPrefs().paletteCollapsed).toBe(false);
    expect(editorPrefs().briefCollapsed).toBe(false);
  });
});
