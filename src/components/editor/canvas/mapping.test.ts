import { describe, expect, it } from "vitest";
import type { Edge, Node } from "@xyflow/react";
import type { ArchNodeData, ConnectionData } from "./types";
import type { ArchitectureDto, ConnectionDto, NodeDto } from "../../../types/architecture";
import { dtoConnectionsToFlow, dtoNodesToFlow, flowToArchitecture, flowToUpdateRequest } from "./mapping";

const dtoNode = (overrides: Partial<NodeDto> = {}): NodeDto => ({
  id: "n1",
  type: "Api",
  name: "Storefront",
  position: { x: 120, y: 40 },
  configuration: { instances: 3 },
  ...overrides,
});

const dtoConnection = (overrides: Partial<ConnectionDto> = {}): ConnectionDto => ({
  id: "c1",
  sourceNodeId: "n1",
  targetNodeId: "n2",
  protocol: "Sql",
  synchronous: true,
  configuration: { timeoutSeconds: 2 },
  ...overrides,
});

const flowNode = (overrides: Partial<Node<ArchNodeData>> = {}): Node<ArchNodeData> => ({
  id: "n1",
  type: "Api",
  position: { x: 120, y: 40 },
  data: { name: "Storefront", configuration: { instances: 3 } },
  ...overrides,
});

const flowEdge = (overrides: Partial<Edge<ConnectionData>> = {}): Edge<ConnectionData> => ({
  id: "c1",
  source: "n1",
  target: "n2",
  data: { protocol: "Sql", synchronous: true, configuration: { timeoutSeconds: 2 } },
  ...overrides,
});

describe("coming in", () => {
  it("carries the position as a nested object, which is the drift that cost data once", () => {
    const [node] = dtoNodesToFlow([dtoNode()]);

    expect(node.position).toEqual({ x: 120, y: 40 });
  });

  it("keeps a position of zero rather than treating it as absent", () => {
    const [node] = dtoNodesToFlow([dtoNode({ position: { x: 0, y: 0 } })]);

    expect(node.position).toEqual({ x: 0, y: 0 });
  });

  it("puts the type where React Flow looks for it and the name where the renderer does", () => {
    const [node] = dtoNodesToFlow([dtoNode()]);

    expect(node.type).toBe("Api");
    expect(node.data.name).toBe("Storefront");
  });

  it("turns an unconfigured node into an empty object, never undefined", () => {
    const [node] = dtoNodesToFlow([dtoNode({ configuration: undefined as never })]);

    expect(node.data.configuration).toEqual({});
  });

  it("renames the two endpoints to source and target", () => {
    const [edge] = dtoConnectionsToFlow([dtoConnection()]);

    expect(edge.source).toBe("n1");
    expect(edge.target).toBe("n2");
    expect(edge.data).toEqual({ protocol: "Sql", synchronous: true, configuration: { timeoutSeconds: 2 } });
  });

  it("keeps an asynchronous call asynchronous", () => {
    const [edge] = dtoConnectionsToFlow([dtoConnection({ synchronous: false })]);

    expect(edge.data?.synchronous).toBe(false);
  });
});

describe("going out", () => {
  it("writes the position back nested, so a round trip does not move anything", () => {
    const request = flowToUpdateRequest("Tienda", 4, [flowNode()], []);

    expect(request.nodes[0].position).toEqual({ x: 120, y: 40 });
  });

  it("survives a full round trip unchanged", () => {
    const nodes = dtoNodesToFlow([dtoNode(), dtoNode({ id: "n2", type: "Database", name: "Db" })]);
    const edges = dtoConnectionsToFlow([dtoConnection()]);

    const request = flowToUpdateRequest("Tienda", 4, nodes, edges);

    expect(request.nodes).toEqual([dtoNode(), dtoNode({ id: "n2", type: "Database", name: "Db" })]);
    expect(request.connections).toEqual([dtoConnection()]);
  });

  it("defaults an edge with no data to a synchronous Http call", () => {
    const request = flowToUpdateRequest("Tienda", 1, [], [flowEdge({ data: undefined })]);

    expect(request.connections[0].protocol).toBe("Http");
    expect(request.connections[0].synchronous).toBe(true);
    expect(request.connections[0].configuration).toEqual({});
  });

  it("carries the name and version it is handed, not the ones it started from", () => {
    const request = flowToUpdateRequest("Renombrada", 9, [flowNode()], []);

    expect(request.name).toBe("Renombrada");
    expect(request.version).toBe(9);
  });
});

describe("the graph on screen", () => {
  const base: ArchitectureDto = {
    name: "As it was opened",
    shareToken: "share-1",
    version: 1,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    nodes: [dtoNode({ name: "Before" })],
    connections: [],
  };

  it("describes the canvas and not the DTO the session was loaded with", () => {
    const current = flowToArchitecture(base, "Live name", 8, [flowNode({ data: { name: "After", configuration: {} } })], []);

    expect(current.nodes[0].name).toBe("After");
    expect(current.name).toBe("Live name");
    expect(current.version).toBe(8);
  });

  it("keeps what the canvas does not hold: the token and the dates", () => {
    const current = flowToArchitecture(base, "Live name", 8, [flowNode()], []);

    expect(current.shareToken).toBe("share-1");
    expect(current.createdAt).toBe(base.createdAt);
  });

  it("does not mutate the architecture it was handed, which is the export's baseline", () => {
    flowToArchitecture(base, "Live name", 8, [flowNode({ data: { name: "After", configuration: {} } })], []);

    expect(base.name).toBe("As it was opened");
    expect(base.nodes[0].name).toBe("Before");
    expect(base.version).toBe(1);
  });
});
