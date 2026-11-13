import { describe, expect, it } from "vitest";
import { INT_MAX, RETRIES_BOUNDS, TIMEOUT_SECONDS_BOUNDS, boundedNumber } from "./bounds";

describe("boundedNumber", () => {
  it("clears the field when there is no number to read", () => {
    expect(boundedNumber("", TIMEOUT_SECONDS_BOUNDS)).toBeNull();
    expect(boundedNumber("   ", TIMEOUT_SECONDS_BOUNDS)).toBeNull();
    expect(boundedNumber("abc", TIMEOUT_SECONDS_BOUNDS)).toBeNull();
    expect(boundedNumber("1e999", RETRIES_BOUNDS)).toBeNull();
  });

  it("keeps a timeout inside the 1–600 the server accepts", () => {
    expect(boundedNumber("45", TIMEOUT_SECONDS_BOUNDS)).toBe(45);
    expect(boundedNumber("700", TIMEOUT_SECONDS_BOUNDS)).toBe(600);
    expect(boundedNumber("0", TIMEOUT_SECONDS_BOUNDS)).toBe(1);
    expect(boundedNumber("-30", TIMEOUT_SECONDS_BOUNDS)).toBe(1);
  });

  it("never sends a negative number of retries", () => {
    expect(boundedNumber("3", RETRIES_BOUNDS)).toBe(3);
    expect(boundedNumber("0", RETRIES_BOUNDS)).toBe(0);
    expect(boundedNumber("-1", RETRIES_BOUNDS)).toBe(0);
  });

  it("rounds every field that does not declare a step, because the server reads an int", () => {
    expect(boundedNumber("1.5", TIMEOUT_SECONDS_BOUNDS)).toBe(2);
    expect(boundedNumber("2.4", RETRIES_BOUNDS)).toBe(2);
    expect(boundedNumber("0.75", { min: 0, max: 1, step: 0.01 })).toBe(0.75);
  });

  it("stops at the largest int the server can read", () => {
    expect(boundedNumber("99999999999999999999", RETRIES_BOUNDS)).toBe(INT_MAX);
    expect(boundedNumber(String(INT_MAX), RETRIES_BOUNDS)).toBe(INT_MAX);
  });

  it("floors at zero when the field declares no minimum", () => {
    expect(boundedNumber("-3", {})).toBe(0);
    expect(boundedNumber("5", {})).toBe(5);
  });
});
