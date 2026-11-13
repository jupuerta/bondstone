import { beforeEach, describe, expect, it } from "vitest";
import { challengeOf, editorPath, rememberWork, savedWork } from "./savedWork";

const work = { editToken: "e1", shareToken: "s1", name: "Mi intento" };

describe("the path back into a saved piece of work", () => {
  it("opens a free architecture in the editor, nothing else", () => {
    expect(editorPath({ editToken: "e1", challengeSlug: null })).toBe("/e/e1");
  });

  it("carries the challenge along, so the editor opens with the band", () => {
    expect(editorPath({ editToken: "e1", challengeSlug: "single-point-of-failure" })).toBe("/e/e1?challenge=single-point-of-failure");
  });

  it("escapes whatever the token and the slug bring", () => {
    expect(editorPath({ editToken: "a/b c", challengeSlug: "x&y" })).toBe("/e/a%2Fb%20c?challenge=x%26y");
  });
});

describe("the challenge a search string names", () => {
  it("is nothing on a plain editor url", () => {
    expect(challengeOf("")).toBeNull();
    expect(challengeOf("?fault=node:1:down")).toBeNull();
    expect(challengeOf("?challenge=")).toBeNull();
  });

  it("is the slug the link carried, beside whatever else travels", () => {
    expect(challengeOf("?challenge=single-point-of-failure")).toBe("single-point-of-failure");
    expect(challengeOf("?fault=node:1:down&challenge=single-point-of-failure")).toBe("single-point-of-failure");
  });

  it("reads back what editorPath wrote", () => {
    const path = editorPath({ editToken: "e1", challengeSlug: "x&y" });

    expect(challengeOf(path.slice(path.indexOf("?")))).toBe("x&y");
  });
});

describe("what a saved piece of work remembers about its challenge", () => {
  beforeEach(() => window.localStorage.clear());

  it("keeps the slug it was started with", () => {
    rememberWork({ ...work, challengeSlug: "single-point-of-failure" });

    expect(savedWork()[0]?.challengeSlug).toBe("single-point-of-failure");
    expect(editorPath(savedWork()[0]!)).toBe("/e/e1?challenge=single-point-of-failure");
  });

  it("does not forget the slug when the editor is opened without it", () => {
    rememberWork({ ...work, challengeSlug: "single-point-of-failure" });
    rememberWork({ ...work, challengeSlug: null });
    rememberWork(work);

    expect(savedWork()).toHaveLength(1);
    expect(savedWork()[0]?.challengeSlug).toBe("single-point-of-failure");
  });

  it("has no slug for a free architecture", () => {
    rememberWork({ ...work, challengeSlug: null });

    expect(editorPath(savedWork()[0]!)).toBe("/e/e1");
  });
});
