import { describe, expect, it } from "vitest";
import { exportFileName, fileSlug } from "./fileName";
import type { ArchitectureDto } from "../types/architecture";

describe("the name a download lands under", () => {
  it("turns a title into something a file system accepts", () => {
    expect(fileSlug("Tienda de Pedidos")).toBe("tienda-de-pedidos");
    expect(fileSlug("  ¡Gateway!  ")).toBe("gateway");
    expect(fileSlug("v2 — API / DB")).toBe("v2-api-db");
  });

  it("falls back rather than producing a file called nothing", () => {
    expect(fileSlug("···")).toBe("architecture");
    expect(fileSlug("")).toBe("architecture");
  });

  it("gives the three files of one architecture the same stem", () => {
    const architecture = { name: "Tienda", version: 7 } as ArchitectureDto;

    expect(exportFileName(architecture, "md")).toBe("tienda-v7.md");
    expect(exportFileName(architecture, "png")).toBe("tienda-v7.png");
    expect(exportFileName(architecture, "svg")).toBe("tienda-v7.svg");
  });
});
