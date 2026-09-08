import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, useCallback, useRef, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { LanguageProvider } from "../i18n";
import { useAutosave, type SaveStatus } from "./useAutosave";
import type { UpdateArchitectureRequest, UpdateArchitectureResult } from "../types/architecture";

const { updateArchitecture } = vi.hoisted(() => ({ updateArchitecture: vi.fn() }));

vi.mock("../api/architectureClient", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/architectureClient")>()),
  updateArchitecture,
}));

const { ConflictError } = await import("../api/architectureClient");

const server = { version: 6 };
const sent: { version: number; keepalive: boolean }[] = [];
let offline = false;

updateArchitecture.mockImplementation(
  (_token: string, request: UpdateArchitectureRequest, options?: { keepalive?: boolean }): Promise<UpdateArchitectureResult> => {
    sent.push({ version: request.version, keepalive: options?.keepalive === true });
    if (offline) return Promise.reject(new TypeError("Failed to fetch"));
    if (request.version !== server.version) return Promise.reject(new ConflictError("changed elsewhere", server.version));
    server.version += 1;
    return Promise.resolve({ version: server.version, updatedAt: "2026-09-08T00:00:00Z" });
  },
);

interface Probe {
  status: SaveStatus;
  version: number;
  error: string | null;
  scheduleSave: () => void;
  retryNow: () => void;
}

let probe: Probe;
const conflicts: number[] = [];

function Harness() {
  const [version, setVersion] = useState(6);
  const versionRef = useRef(version);
  versionRef.current = version;

  const getSnapshot = useCallback(
    (): UpdateArchitectureRequest => ({ name: "Tienda", version: versionRef.current, nodes: [], connections: [] }),
    [],
  );
  const onSaved = useCallback((result: UpdateArchitectureResult) => setVersion(result.version), []);
  const onConflict = useCallback((currentVersion: number) => conflicts.push(currentVersion), []);
  const { status, error, scheduleSave, retryNow } = useAutosave({ editToken: "tok", enabled: true, getSnapshot, onSaved, onConflict });

  probe = { status, version, error, scheduleSave, retryNow };
  return null;
}

let container: HTMLDivElement;
let root: Root;

const visibility = (value: "visible" | "hidden") =>
  act(async () => {
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => value });
    document.dispatchEvent(new Event("visibilitychange"));
    await Promise.resolve();
  });

const settle = () => act(async () => { await Promise.resolve(); });
const debounce = () => act(async () => { await vi.advanceTimersByTimeAsync(1500); });

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.useFakeTimers();
  server.version = 6;
  sent.length = 0;
  conflicts.length = 0;
  offline = false;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  act(() => root.render(<LanguageProvider><Harness /></LanguageProvider>));
});

afterEach(async () => {
  await visibility("visible");
  act(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

describe("saving what is pending when the tab goes away", () => {
  it("keeps the version the server handed back, so the next edit does not fight itself", async () => {
    await act(async () => probe.scheduleSave());
    expect(probe.status).toBe("pending");

    await visibility("hidden");
    await settle();

    expect(sent).toEqual([{ version: 6, keepalive: true }]);
    expect(probe.version).toBe(7);
    expect(probe.status).toBe("saved");

    await visibility("visible");
    await act(async () => probe.scheduleSave());
    await debounce();

    expect(sent[1]).toEqual({ version: 7, keepalive: false });
    expect(conflicts).toEqual([]);
    expect(probe.status).toBe("saved");
    expect(probe.version).toBe(8);
  });

  it("does not send the debounced save twice", async () => {
    await act(async () => probe.scheduleSave());
    await visibility("hidden");
    await settle();
    await debounce();

    expect(sent).toHaveLength(1);
  });

  it("sends nothing when there is nothing pending", async () => {
    await visibility("hidden");
    await settle();

    expect(sent).toEqual([]);
    expect(probe.status).toBe("idle");
  });

  it("says so when the save fails while hidden, and the retry picks it up", async () => {
    offline = true;
    await act(async () => probe.scheduleSave());
    await visibility("hidden");
    await settle();

    expect(probe.status).toBe("error");
    expect(probe.error).not.toBeNull();

    offline = false;
    await visibility("visible");
    await act(async () => probe.retryNow());
    await settle();

    expect(sent).toEqual([{ version: 6, keepalive: true }, { version: 6, keepalive: false }]);
    expect(probe.version).toBe(7);
    expect(probe.status).toBe("saved");
  });

  it("still reports a real conflict from another tab", async () => {
    server.version = 42;
    await act(async () => probe.scheduleSave());
    await visibility("hidden");
    await settle();

    expect(conflicts).toEqual([42]);
    expect(probe.status).toBe("conflict");
  });
});
