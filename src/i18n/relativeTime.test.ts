import { describe, expect, it } from "vitest";
import { relativeTime } from "./relativeTime";

const now = new Date("2026-01-20T12:00:00Z").getTime();
const ago = (ms: number) => now - ms;

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

describe("in Spanish", () => {
  it("says 'ahora' under a minute", () => {
    expect(relativeTime(ago(40 * SECOND), "es", now)).toBe("ahora");
  });

  it("picks the largest unit that fits, and pluralises it", () => {
    expect(relativeTime(ago(MINUTE), "es", now)).toBe("hace 1 minuto");
    expect(relativeTime(ago(5 * MINUTE), "es", now)).toBe("hace 5 minutos");
    expect(relativeTime(ago(3 * HOUR), "es", now)).toBe("hace 3 horas");
    expect(relativeTime(ago(4 * DAY), "es", now)).toBe("hace 4 días");
  });

  it("uses the word for it when the language has one", () => {
    expect(relativeTime(ago(DAY), "es", now)).toBe("ayer");
    expect(relativeTime(ago(2 * DAY), "es", now)).toBe("anteayer");
  });
});

describe("in English", () => {
  it("reads the same moments in its own words", () => {
    expect(relativeTime(ago(40 * SECOND), "en", now)).toBe("now");
    expect(relativeTime(ago(MINUTE), "en", now)).toBe("1 minute ago");
    expect(relativeTime(ago(3 * HOUR), "en", now)).toBe("3 hours ago");
    expect(relativeTime(ago(DAY), "en", now)).toBe("yesterday");
    expect(relativeTime(ago(4 * DAY), "en", now)).toBe("4 days ago");
  });
});

it("reads a timestamp from the future as 'now' rather than counting down to it", () => {
  expect(relativeTime(now + 3 * HOUR, "es", now)).toBe("ahora");
});
