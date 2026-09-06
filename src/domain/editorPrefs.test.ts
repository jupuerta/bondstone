import { beforeEach, describe, expect, it } from "vitest";
import { editorPrefs, rememberEditorPrefs } from "./editorPrefs";

describe("what the editor remembers about its own layout", () => {
  beforeEach(() => window.localStorage.clear());

  it("starts with the palette open, the legend closed and the brief unfolded", () => {
    expect(editorPrefs()).toEqual({ paletteCollapsed: false, legendOpen: false, briefCollapsed: false });
  });

  it("keeps the palette folded across reloads, in one key", () => {
    rememberEditorPrefs({ paletteCollapsed: true });
    rememberEditorPrefs({ legendOpen: true });
    rememberEditorPrefs({ briefCollapsed: true });
    expect(editorPrefs()).toEqual({ paletteCollapsed: true, legendOpen: true, briefCollapsed: true });
    expect(Object.keys(window.localStorage)).toEqual(["archforge.editor.ui"]);
  });

  it("ignores junk", () => {
    window.localStorage.setItem("archforge.editor.ui", "{nope");
    expect(editorPrefs()).toEqual({ paletteCollapsed: false, legendOpen: false, briefCollapsed: false });
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ paletteCollapsed: "yes", briefCollapsed: 1 }));
    expect(editorPrefs().paletteCollapsed).toBe(false);
    expect(editorPrefs().briefCollapsed).toBe(false);
  });
});
