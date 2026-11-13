import { savedWork } from "./savedWork";

/** Un reto cuenta como empezado si hay un intento guardado en este navegador. */
export function startedChallenges(): Set<string> {
  return new Set(
    savedWork()
      .map((entry) => entry.challengeSlug)
      .filter((slug): slug is string => slug !== null),
  );
}

const PASSED_KEY = "bondstone.challenges.passed";

function readPassed(): string[] {
  try {
    const raw = window.localStorage.getItem(PASSED_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((slug): slug is string => typeof slug === "string");
  } catch {
    return [];
  }
}

/** Los retos superados que este navegador recuerda. La clave la escribe el editor al evaluar. */
export function passedChallenges(): Set<string> {
  return new Set(readPassed());
}

export function rememberPassed(slug: string): void {
  try {
    const passed = readPassed();
    if (passed.includes(slug)) return;
    window.localStorage.setItem(PASSED_KEY, JSON.stringify([...passed, slug]));
  } catch {
    return;
  }
}
