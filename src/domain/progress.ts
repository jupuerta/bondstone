import { savedWork } from "./savedWork";

/** Un reto cuenta como empezado si hay un intento guardado en este navegador. */
export function startedChallenges(): Set<string> {
  return new Set(
    savedWork()
      .map((entry) => entry.challengeSlug)
      .filter((slug): slug is string => slug !== null),
  );
}

const PASSED_KEY = "archforge.challenges.passed";

/** Los retos superados que este navegador recuerda. La clave la escribe el editor al evaluar. */
export function passedChallenges(): Set<string> {
  try {
    const raw = window.localStorage.getItem(PASSED_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return new Set(Array.isArray(parsed) ? parsed.filter((slug): slug is string => typeof slug === "string") : []);
  } catch {
    return new Set();
  }
}
