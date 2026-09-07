import { describe, expect, it } from "vitest";
import { NODE_META } from "../palette/nodeMeta";
import {
  HEAD,
  MARK,
  PANELS,
  PANEL_INSET,
  PATTERNS,
  PATTERN_FOR_CODE,
  PATTERN_IDS,
  PLATE,
  headTransform,
  linkRun,
  markBox,
  marksOf,
  plateBox,
  shaftEnd,
  type Box,
  type PatternScene,
} from "./patterns";

const scenes: Array<[string, PatternScene, Box]> = PATTERN_IDS.flatMap((id) => [
  [`${id} · before`, PATTERNS[id].before, PANELS.before] as [string, PatternScene, Box],
  [`${id} · after`, PATTERNS[id].after, PANELS.after] as [string, PatternScene, Box],
]);

const MECHANISM_DRAWINGS = [
  "bounded-call",
  "retried-call",
  "breaker-on-call",
  "capped-entry",
  "dead-letter-branch",
  "read-replicas",
];

describe("the eleven pattern drawings (V16 C8, V19 F5)", () => {
  it("are eleven, and each one answers codes no other one answers", () => {
    expect(PATTERN_IDS).toHaveLength(11);

    const codes = PATTERN_IDS.flatMap((id) => PATTERNS[id].codes);
    expect(codes).toHaveLength(new Set(codes).size);
  });

  it("leaves the finding-to-drawing map exactly where V16 left it", () => {
    for (const id of MECHANISM_DRAWINGS) expect(PATTERNS[id as (typeof PATTERN_IDS)[number]].codes).toEqual([]);

    expect(PATTERN_FOR_CODE).toEqual({
      SPOF: "balanced-instances",
      SYNC_CHAIN: "queue-between",
      MISSING_RESILIENCE: "queue-between",
      ASYNC_NO_QUEUE: "queue-between",
      MISSING_CACHE: "cache-in-front",
      UNDERSIZED_COMPONENT: "cache-in-front",
      EXTERNAL_NO_FALLBACK: "external-fallback",
      EXPOSED_DATABASE: "gate-in-front",
      UNAUTHENTICATED_ENTRY: "gate-in-front",
    });
  });

  it("show a fix, which means the right-hand drawing says something the left one does not", () => {
    for (const id of PATTERN_IDS) {
      const { before, after } = PATTERNS[id];

      expect(after.nodes.length, `${id}: fewer boxes after`).toBeGreaterThanOrEqual(before.nodes.length);
      expect(after.links.length, `${id}: fewer calls after`).toBeGreaterThanOrEqual(before.links.length);
      expect(said(after), `${id}: the two panels say the same thing`).not.toBe(said(before));
    }
  });

  it("draw components the canvas knows, so a queue here is the queue there", () => {
    for (const [name, scene] of scenes) {
      for (const node of scene.nodes) {
        expect(NODE_META[node.type], `${name}: ${node.type}`).toBeDefined();
      }
    }
  });

  it("name every end of every link", () => {
    for (const [name, scene] of scenes) {
      const ids = scene.nodes.map((node) => node.id);
      expect(new Set(ids).size, `${name}: duplicate node id`).toBe(ids.length);

      for (const link of scene.links) {
        expect(ids, `${name}: ${link.from} ->`).toContain(link.from);
        expect(ids, `${name}: -> ${link.to}`).toContain(link.to);
      }
    }
  });
});

describe("every plate", () => {
  it("stays inside its panel", () => {
    for (const [name, scene, panel] of scenes) {
      for (const node of scene.nodes) {
        const box = plateBox(node);
        expect(box.x, `${name}: ${node.id} off the left`).toBeGreaterThanOrEqual(panel.x + PANEL_INSET);
        expect(box.y, `${name}: ${node.id} off the top`).toBeGreaterThanOrEqual(panel.y + PANEL_INSET);
        expect(box.x + box.width, `${name}: ${node.id} off the right`).toBeLessThanOrEqual(
          panel.x + panel.width - PANEL_INSET,
        );
        expect(box.y + box.height, `${name}: ${node.id} off the bottom`).toBeLessThanOrEqual(
          panel.y + panel.height - PANEL_INSET,
        );
      }
    }
  });

  it("keeps clear of its neighbours", () => {
    for (const [name, scene] of scenes) {
      for (const [a, b] of pairs(scene.nodes)) {
        const gap = Math.max(
          Math.abs(a.x - b.x) - PLATE,
          Math.abs(a.y - b.y) - PLATE,
        );
        expect(gap, `${name}: ${a.id} and ${b.id} are touching`).toBeGreaterThanOrEqual(2);
      }
    }
  });
});

describe("every badge", () => {
  it("stays inside its panel", () => {
    for (const [name, scene, panel] of scenes) {
      for (const { mark, at } of marksOf(scene)) {
        const box = markBox(at);
        expect(box.x, `${name}: ${mark} off the left`).toBeGreaterThanOrEqual(panel.x + PANEL_INSET);
        expect(box.y, `${name}: ${mark} off the top`).toBeGreaterThanOrEqual(panel.y + PANEL_INSET);
        expect(box.x + box.width, `${name}: ${mark} off the right`).toBeLessThanOrEqual(
          panel.x + panel.width - PANEL_INSET,
        );
        expect(box.y + box.height, `${name}: ${mark} off the bottom`).toBeLessThanOrEqual(
          panel.y + panel.height - PANEL_INSET,
        );
      }
    }
  });

  it("sits beside the drawing and not on top of a box", () => {
    for (const [name, scene] of scenes) {
      for (const { mark, at } of marksOf(scene)) {
        const badge = markBox(at);

        for (const node of scene.nodes) {
          const plate = plateBox(node);
          const overlaps =
            badge.x < plate.x + plate.width &&
            badge.x + badge.width > plate.x &&
            badge.y < plate.y + plate.height &&
            badge.y + badge.height > plate.y;

          expect(overlaps, `${name}: ${mark} covers ${node.id}`).toBe(false);
        }
      }
    }
  });

  it("is smaller than the boxes it annotates", () => {
    expect(MARK).toBeLessThan(PLATE);
  });
});

describe("every link", () => {
  it("has a run long enough to read as an arrow and not just a head", () => {
    for (const [name, scene] of scenes) {
      for (const link of scene.links) {
        const { length } = linkRun(scene, link);
        expect(length, `${name}: ${link.from} -> ${link.to}`).toBeGreaterThanOrEqual(HEAD.length + 2);
      }
    }
  });

  it("starts and ends outside both plates it joins", () => {
    for (const [name, scene] of scenes) {
      for (const link of scene.links) {
        const { from, to } = linkRun(scene, link);
        for (const node of [scene.nodes.find((n) => n.id === link.from)!, scene.nodes.find((n) => n.id === link.to)!]) {
          const box = plateBox(node);
          for (const point of [from, to]) {
            const inside =
              point.x > box.x && point.x < box.x + box.width && point.y > box.y && point.y < box.y + box.height;
            expect(inside, `${name}: ${link.from} -> ${link.to} starts inside ${node.id}`).toBe(false);
          }
        }
      }
    }
  });

  it("stops its shaft short of the tip, so the head keeps its point", () => {
    const from = { x: 0, y: 0 };
    const to = { x: 10, y: 0 };
    expect(shaftEnd(from, to)).toEqual({ x: 10 - HEAD.length * 0.8, y: 0 });
    expect(shaftEnd({ x: 0, y: 0 }, { x: 1, y: 0 })).toEqual({ x: 0, y: 0 });
  });

  it("points the head the way it runs", () => {
    expect(headTransform({ x: 0, y: 0 }, { x: 10, y: 0 })).toBe("translate(10 0) rotate(0)");
    expect(headTransform({ x: 0, y: 0 }, { x: 0, y: 10 })).toBe("translate(0 10) rotate(90)");
    expect(headTransform({ x: 10, y: 10 }, { x: 0, y: 0 })).toBe("translate(0 0) rotate(-135)");
  });
});

describe("each panel's drawing", () => {
  it("sits in the middle of it", () => {
    for (const [name, scene, panel] of scenes) {
      const boxes = scene.nodes.map(plateBox);
      const left = Math.min(...boxes.map((box) => box.x));
      const right = Math.max(...boxes.map((box) => box.x + box.width));
      const top = Math.min(...boxes.map((box) => box.y));
      const bottom = Math.max(...boxes.map((box) => box.y + box.height));

      expect((left + right) / 2, `${name}: off-centre across`).toBeCloseTo(panel.x + panel.width / 2, 0);
      expect((top + bottom) / 2, `${name}: off-centre down`).toBeCloseTo(panel.y + panel.height / 2, 0);
    }
  });
});

function said(scene: PatternScene): string {
  return JSON.stringify(scene);
}

function pairs<T>(items: T[]): Array<[T, T]> {
  return items.flatMap((item, index) => items.slice(index + 1).map((other) => [item, other] as [T, T]));
}
