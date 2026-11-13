import { describe, expect, it } from "vitest";
import { mermaidIdentifier, mermaidLabel, tableCell } from "./markdownEscape";

describe("a table cell", () => {
  it("escapes a pipe, which would end the cell", () => {
    expect(tableCell("Orders | Payments")).toBe("Orders \\| Payments");
  });

  it("turns a newline into a space, because it would end the row", () => {
    expect(tableCell("Orders\r\nAPI")).toBe("Orders API");
  });

  it("is empty for nothing at all", () => {
    expect(tableCell(null)).toBe("");
    expect(tableCell(undefined)).toBe("");
  });
});

describe("a mermaid label", () => {
  it("turns brackets into parentheses", () => {
    expect(mermaidLabel("API [v2]")).toBe("API (v2)");
    expect(mermaidLabel("API {v2}")).toBe("API (v2)");
  });

  it("turns a quote into an entity", () => {
    expect(mermaidLabel('the "fast" path')).toBe("the #quot;fast#quot; path");
  });

  it("replaces a pipe, which would start an edge label", () => {
    expect(mermaidLabel("Orders | Payments")).toBe("Orders / Payments");
  });
});

describe("a mermaid identifier", () => {
  it.each([
    ["Orders API", "OrdersAPI"],
    ["Órdenes-Núcleo", "rdenesNcleo"],
    ["API [v2]", "APIv2"],
  ])("keeps only what mermaid accepts: %s", (name, expected) => {
    expect(mermaidIdentifier(name)).toBe(expected);
  });

  it("never starts with a digit", () => {
    expect(mermaidIdentifier("2FA")).toBe("n2FA");
  });

  it("is never empty, however unusable the name", () => {
    expect(mermaidIdentifier("¿?¡!")).toBe("n");
  });
});
