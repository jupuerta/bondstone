import { beforeEach, describe, expect, it } from "vitest";
import { editorPrefs, rememberEditorPrefs } from "./editorPrefs";

describe("what the editor remembers about its own layout", () => {
  beforeEach(() => window.localStorage.clear());

  it("starts with the palette open", () => {
    expect(editorPrefs()).toEqual({ paletteCollapsed: false });
  });

  it("keeps the palette folded across reloads, in one key", () => {
    rememberEditorPrefs({ paletteCollapsed: true });
    expect(editorPrefs().paletteCollapsed).toBe(true);
    expect(Object.keys(window.localStorage)).toEqual(["archforge.editor.ui"]);
  });

  it("ignores junk", () => {
    window.localStorage.setItem("archforge.editor.ui", "{nope");
    expect(editorPrefs()).toEqual({ paletteCollapsed: false });
    window.localStorage.setItem("archforge.editor.ui", JSON.stringify({ paletteCollapsed: "yes" }));
    expect(editorPrefs().paletteCollapsed).toBe(false);
  });
});
