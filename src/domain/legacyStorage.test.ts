import { beforeEach, describe, expect, it } from "vitest";
import { migrateLegacyKeys } from "./legacyStorage";

describe("lo guardado bajo el nombre viejo", () => {
  beforeEach(() => window.localStorage.clear());

  it("pasa al nombre nuevo y no deja rastro del viejo", () => {
    window.localStorage.setItem("archforge.architectures", "[1]");
    window.localStorage.setItem("archforge.editor.ui", '{"appearance":"light"}');
    migrateLegacyKeys();
    expect(window.localStorage.getItem("bondstone.architectures")).toBe("[1]");
    expect(window.localStorage.getItem("bondstone.editor.ui")).toBe('{"appearance":"light"}');
    expect(Object.keys(window.localStorage).sort()).toEqual(["bondstone.architectures", "bondstone.editor.ui"]);
  });

  it("no toca lo que ya está bajo el nombre nuevo", () => {
    window.localStorage.setItem("bondstone.architectures", "[1]");
    window.localStorage.setItem("bondstone.challenges.passed", "[]");
    migrateLegacyKeys();
    expect(window.localStorage.getItem("bondstone.architectures")).toBe("[1]");
    expect(window.localStorage.getItem("bondstone.challenges.passed")).toBe("[]");
  });

  it("deja ganar al nombre nuevo cuando existen los dos", () => {
    window.localStorage.setItem("archforge.architectures", "[viejo]");
    window.localStorage.setItem("bondstone.architectures", "[nuevo]");
    migrateLegacyKeys();
    expect(window.localStorage.getItem("bondstone.architectures")).toBe("[nuevo]");
    expect(window.localStorage.getItem("archforge.architectures")).toBeNull();
  });

  it("no se lleva por delante lo que nunca llevó el nombre", () => {
    window.localStorage.setItem("otra.cosa", "1");
    migrateLegacyKeys();
    expect(window.localStorage.getItem("otra.cosa")).toBe("1");
  });
});
