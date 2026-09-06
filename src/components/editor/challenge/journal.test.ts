import { describe, expect, it } from "vitest";
import { journalOf, lessonKey, mechanismsOf } from "./journal";
import type { ComparableGraph } from "./graphDiff";
import { MECHANISMS, MECHANISM_IDS } from "../../../domain/mechanisms";
import type {
  AnalysisResult,
  ArchitectureSnapshot,
  ChallengeDetail,
  ConnectionDto,
  NodeDto,
  NodeType,
} from "../../../types/architecture";
import es from "../../../i18n/es.json";
import en from "../../../i18n/en.json";

let ids = 0;
const id = () => `n${(ids += 1)}`;

function node(name: string, type: NodeType = "Service"): NodeDto {
  return { id: id(), type, name, position: { x: 0, y: 0 } };
}

function call(from: NodeDto, to: NodeDto, synchronous = true): ConnectionDto {
  return { id: id(), sourceNodeId: from.id, targetNodeId: to.id, protocol: "Http", synchronous };
}

const graph = (nodes: NodeDto[], connections: ConnectionDto[] = []): ComparableGraph => ({ nodes, connections });

const definition = (slug: string, topic: ChallengeDetail["topic"] = "Resilience"): ChallengeDetail => ({
  slug,
  title: { key: `challenge.${slug}.title` },
  story: { key: `challenge.${slug}.story` },
  difficulty: "intermediate",
  topic,
  requirements: [],
  optionalGoals: [],
  hints: [],
});

const snapshot = (score: number | null, areas: ArchitectureSnapshot["areas"]): ArchitectureSnapshot => ({
  version: 2,
  scoringVersion: "v1",
  score,
  summary: null,
  createdAt: "",
  areas,
});

const analysis = (score: number | null, areas: AnalysisResult["areas"]): AnalysisResult => ({
  scoringVersion: "v1",
  score,
  areas,
  summary: { critical: 0, warning: 0, info: 0, passed: 0 },
  findings: [],
});

describe("journalOf · what you changed", () => {
  it("says nothing when the starting point was never kept", () => {
    const api = node("Api", "Api");

    const journal = journalOf({
      definition: definition("email-can-wait"),
      startingPoint: null,
      current: graph([api]),
      first: null,
      analysis: null,
    });

    expect(journal.changes).toEqual([]);
    expect(journal.kept).toBe(false);
  });

  it("reads the walk from the starting point in the change voice, never the reference one", () => {
    const startApi = node("Api", "Api");
    const startWorker = node("Worker", "Worker");
    const nowApi = node("Api", "Api");
    const nowWorker = node("Worker", "Worker");

    const journal = journalOf({
      definition: definition("email-can-wait"),
      startingPoint: graph([startApi, startWorker], [call(startApi, startWorker)]),
      current: graph([nowApi, nowWorker], [call(nowApi, nowWorker, false)]),
      first: null,
      analysis: null,
    });

    expect(journal.kept).toBe(true);
    expect(journal.changes.map((change) => change.text.key)).toEqual(["ui.change.diff.callAsync"]);
    expect(journal.changes.every((change) => !change.text.key.startsWith("ui.compare."))).toBe(true);
  });
});

describe("journalOf · what moved", () => {
  const topicOnly = (score: number) => [{ area: "Resilience" as const, score, state: "Scored" as const }];

  it("puts the area the challenge is about before the overall score", () => {
    const journal = journalOf({
      definition: definition("email-can-wait", "Resilience"),
      startingPoint: null,
      current: graph([]),
      first: snapshot(62, { Resilience: 40 }),
      analysis: analysis(88, topicOnly(91)),
    });

    expect(journal.moves).toEqual([
      { what: "area", area: "Resilience", from: 40, to: 91 },
      { what: "score", from: 62, to: 88 },
    ]);
  });

  it("says nothing about a number that did not move", () => {
    const journal = journalOf({
      definition: definition("email-can-wait", "Resilience"),
      startingPoint: null,
      current: graph([]),
      first: snapshot(70, { Resilience: 50 }),
      analysis: analysis(70, topicOnly(50)),
    });

    expect(journal.moves).toEqual([]);
  });

  it("refuses to subtract across two scoring versions", () => {
    const journal = journalOf({
      definition: definition("email-can-wait", "Resilience"),
      startingPoint: null,
      current: graph([]),
      first: { ...snapshot(10, { Resilience: 10 }), scoringVersion: "v0" },
      analysis: analysis(90, topicOnly(90)),
    });

    expect(journal.moves).toEqual([]);
  });

  it("leaves an area out while the analyzer has no rules to score it with", () => {
    const journal = journalOf({
      definition: definition("email-can-wait", "Resilience"),
      startingPoint: null,
      current: graph([]),
      first: snapshot(62, { Resilience: 40 }),
      analysis: analysis(88, [{ area: "Resilience", score: null, state: "NotApplicable" }]),
    });

    expect(journal.moves).toEqual([{ what: "score", from: 62, to: 88 }]);
  });

  it("needs a first snapshot: without one there is no before to subtract from", () => {
    const journal = journalOf({
      definition: definition("email-can-wait", "Resilience"),
      startingPoint: null,
      current: graph([]),
      first: null,
      analysis: analysis(88, topicOnly(91)),
    });

    expect(journal.moves).toEqual([]);
  });
});

describe("the lesson every challenge already had written", () => {
  const slugs = [...new Set(MECHANISM_IDS.flatMap((mechanism) => MECHANISMS[mechanism].challenges))];

  it("names a phrase that exists in both catalogues for every challenge the mechanisms point at", () => {
    for (const slug of slugs) {
      expect(es, `${slug} in es`).toHaveProperty(lessonKey(slug));
      expect(en, `${slug} in en`).toHaveProperty(lessonKey(slug));
    }
  });

  it("asks for no argument, because a journal has none to hand it", () => {
    for (const slug of slugs) {
      expect((es as Record<string, unknown>)[lessonKey(slug)]).not.toMatch(/\{/);
      expect((en as Record<string, unknown>)[lessonKey(slug)]).not.toMatch(/\{/);
    }
  });

  it("has one for every challenge the catalogue ships, not only the ones with a mechanism", () => {
    const shipped = Object.keys(es)
      .filter((key) => /^challenge\.[a-z0-9-]+\.title$/.test(key))
      .map((key) => key.split(".")[1]);

    expect(shipped.length).toBeGreaterThan(slugs.length);
    for (const slug of shipped) {
      expect(es, `${slug} in es`).toHaveProperty(lessonKey(slug));
      expect(en, `${slug} in en`).toHaveProperty(lessonKey(slug));
    }
  });
});

describe("mechanismsOf", () => {
  it("gives the sheets that name the challenge, in the order the workshop lays them out", () => {
    expect(mechanismsOf("retry-storm")).toEqual(["retries", "circuitBreaker"]);
    expect(mechanismsOf("backlog")).toEqual(["queue", "readReplicas", "instances"]);
  });

  it("gives nothing rather than a wrong sheet for a challenge no mechanism claims", () => {
    expect(mechanismsOf("god-service")).toEqual([]);
    expect(mechanismsOf("exposed-database")).toEqual([]);
  });
});
