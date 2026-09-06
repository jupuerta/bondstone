import { describe, expect, it } from "vitest";
import type { Edge, Node } from "@xyflow/react";
import type { ArchNodeData, ConnectionData } from "./types";
import {
  EMPTY_HISTORY,
  canRedo,
  canUndo,
  fingerprint,
  record,
  redo,
  sameGraph,
  undo,
  type Graph,
} from "./graphHistory";

const node = (id: string, overrides: Partial<Node<ArchNodeData>> = {}): Node<ArchNodeData> => ({
  id,
  type: "Api",
  position: { x: 0, y: 0 },
  data: { name: id, configuration: {} },
  ...overrides,
});

const edge = (id: string, overrides: Partial<Edge<ConnectionData>> = {}): Edge<ConnectionData> => ({
  id,
  source: "a",
  target: "b",
  data: { synchronous: true, configuration: {} },
  ...overrides,
});

const graph = (nodes: Node<ArchNodeData>[] = [], edges: Edge<ConnectionData>[] = []): Graph => ({ nodes, edges });

describe("what counts as a different canvas", () => {
  it("does not count selecting a component", () => {
    expect(sameGraph(graph([node("a")]), graph([node("a", { selected: true })]))).toBe(true);
  });

  it("does not count a component being dragged, only where it lands", () => {
    const dragging = graph([node("a", { dragging: true })]);

    expect(sameGraph(graph([node("a")]), dragging)).toBe(true);
  });

  it("counts a component that moved", () => {
    const moved = graph([node("a", { position: { x: 120, y: 0 } })]);

    expect(sameGraph(graph([node("a")]), moved)).toBe(false);
  });

  it("counts a rename, a reconfiguration and a deletion", () => {
    expect(sameGraph(graph([node("a")]), graph([node("a", { data: { name: "Other", configuration: {} } })]))).toBe(false);
    expect(sameGraph(graph([node("a")]), graph([node("a", { data: { name: "a", configuration: { instances: 3 } } })]))).toBe(false);
    expect(sameGraph(graph([node("a")]), graph([]))).toBe(false);
  });

  it("counts a call whose direction changed", () => {
    expect(sameGraph(graph([], [edge("c")]), graph([], [edge("c", { data: { synchronous: false, configuration: {} } })]))).toBe(false);
  });

  it("gives an unconfigured element the same print as an explicitly empty one", () => {
    const undefinedConfig = graph([node("a", { data: { name: "a", configuration: undefined as never } })]);

    expect(fingerprint(undefinedConfig)).toBe(fingerprint(graph([node("a")])));
  });
});

describe("walking back and forward", () => {
  const one = graph([node("a")]);
  const two = graph([node("a"), node("b")]);
  const three = graph([node("a"), node("b"), node("c")]);

  it("has nothing to undo before anything happened", () => {
    expect(canUndo(EMPTY_HISTORY)).toBe(false);
    expect(undo(EMPTY_HISTORY, one)).toBeNull();
  });

  it("hands back the state the canvas left", () => {
    const history = record(EMPTY_HISTORY, one);
    const stepped = undo(history, two);

    expect(stepped?.graph).toBe(one);
    expect(canUndo(stepped!.history)).toBe(false);
    expect(canRedo(stepped!.history)).toBe(true);
  });

  it("walks back through several steps in order", () => {
    let history = record(record(EMPTY_HISTORY, one), two);

    const first = undo(history, three)!;
    expect(first.graph).toBe(two);

    const second = undo(first.history, first.graph)!;
    expect(second.graph).toBe(one);
    expect(undo(second.history, second.graph)).toBeNull();
  });

  it("puts back what undo took, and no more", () => {
    const history = record(EMPTY_HISTORY, one);
    const stepped = undo(history, two)!;

    const forward = redo(stepped.history, stepped.graph)!;

    expect(forward.graph).toBe(two);
    expect(canRedo(forward.history)).toBe(false);
    expect(canUndo(forward.history)).toBe(true);
  });

  it("drops the abandoned branch when you edit after undoing", () => {
    const history = record(EMPTY_HISTORY, one);
    const stepped = undo(history, two)!;
    expect(canRedo(stepped.history)).toBe(true);

    const edited = record(stepped.history, stepped.graph);

    expect(canRedo(edited)).toBe(false);
  });

  it("has nothing to redo until something has been undone", () => {
    expect(redo(record(EMPTY_HISTORY, one), two)).toBeNull();
  });
});

describe("the depth limit", () => {
  it("keeps the most recent states and drops the oldest", () => {
    let history = EMPTY_HISTORY;
    for (let i = 0; i < 8; i++) history = record(history, graph([node(`n${i}`)]), 3);

    expect(history.past).toHaveLength(3);
    expect(history.past.map((entry) => entry.nodes[0].id)).toEqual(["n5", "n6", "n7"]);
  });
});
