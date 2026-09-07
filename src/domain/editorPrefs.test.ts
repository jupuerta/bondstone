import { beforeEach, describe, expect, it } from "vitest";
import { editorPrefs, rememberEditorPrefs } from "./editorPrefs";

const DEFAULTS = { paletteCollapsed: false, legendOpen: false, briefCollapsed: false, glossesOpen: false, theme: "dark", panelWidth: 336 };

describe("what the editor remembers about its own layout", () => {
  beforeEach(() => window.localStorage.clear());

  it("starts with the palette open, the legend closed, the brief unfolded, dark, and the panel at 336", () => {
    expect(editorPrefs()).toEqual(DEFAULTS);
  });

  it("keeps the palette folded across reloads, in one key", () => {
    rememberEditorPrefs({ paletteCollapsed: true });
    rememberEditorPrefs({ legendOpen: true });
    rememberEditorPrefs({ briefCollapsed: true, glossesOpen: false });
    expect(editorPrefs()).toEqual({ ...DEFAULTS, paletteCollapsed: true, legendOpen: true, briefCollapsed: true });
    expect(Object.keys(window.localStorage)).toEqual(["archforge.editor.ui"]);
  });

  it("remembers the light theme, and anything else means dark", () => {
    rememberEditorPrefs({ theme: "light" });
    expect(editorPrefs().theme).toBe("light");
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ theme: "system" }));
    expect(editorPrefs().theme).toBe("dark");
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

  it("ignores junk", () => {
    window.localStorage.setItem("archforge.editor.ui", "{nope");
    expect(editorPrefs()).toEqual(DEFAULTS);
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ paletteCollapsed: "yes", briefCollapsed: 1 }));
    expect(editorPrefs().paletteCollapsed).toBe(false);
    expect(editorPrefs().briefCollapsed).toBe(false);
  });
});
