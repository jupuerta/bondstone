const KEY = "archforge.architectures";

const CHALLENGE_PARAM = "challenge";

const LIMIT = 30;

export interface SavedWork {
  editToken: string;
  shareToken: string;
  name: string;
  challengeSlug: string | null;
  createdAt: number;
  updatedAt: number;
  score: number | null;
}

export type NewWork = Omit<SavedWork, "createdAt" | "updatedAt" | "score"> & {
  challengeSlug?: string | null;
};

export function savedWork(): SavedWork[] {
  return read();
}

export function rememberWork(entry: NewWork): void {
  const now = Date.now();
  const entries = read();
  const previous = entries.find((candidate) => candidate.editToken === entry.editToken);

  write([
    {
      editToken: entry.editToken,
      shareToken: entry.shareToken,
      name: entry.name,
      challengeSlug: entry.challengeSlug ?? previous?.challengeSlug ?? null,
      createdAt: previous?.createdAt ?? now,
      updatedAt: previous?.updatedAt ?? now,
      score: previous?.score ?? null,
    },
    ...entries.filter((candidate) => candidate.editToken !== entry.editToken),
  ]);
}

export function touchWork(editToken: string, patch: { name?: string }): void {
  const entries = read();
  const entry = entries.find((candidate) => candidate.editToken === editToken);
  if (!entry) return;

  write([
    { ...entry, ...patch, updatedAt: Date.now() },
    ...entries.filter((candidate) => candidate.editToken !== editToken),
  ]);
}

export function scoreWork(editToken: string, score: number | null): void {
  const entries = read();
  const entry = entries.find((candidate) => candidate.editToken === editToken);
  if (!entry || entry.score === score) return;

  write(entries.map((candidate) => (candidate.editToken === editToken ? { ...candidate, score } : candidate)));
}

export function forgetWork(editToken: string): void {
  write(read().filter((entry) => entry.editToken !== editToken));
}

export function attemptsOf(slug: string): SavedWork[] {
  return read().filter((entry) => entry.challengeSlug === slug);
}

export function editorPath(entry: Pick<SavedWork, "editToken" | "challengeSlug">): string {
  const path = `/e/${encodeURIComponent(entry.editToken)}`;
  return entry.challengeSlug ? `${path}?${CHALLENGE_PARAM}=${encodeURIComponent(entry.challengeSlug)}` : path;
}

export function challengeOf(search: string): string | null {
  return new URLSearchParams(search).get(CHALLENGE_PARAM) || null;
}

function read(): SavedWork[] {
  try {
    const raw = window.localStorage.getItem(KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(parsed)) return [];

    return parsed.filter(isSavedWork).map(withScore).sort(byNewestEdit);
  } catch {
    return [];
  }
}

function withScore(entry: SavedWork): SavedWork {
  return entry.score === undefined ? { ...entry, score: null } : entry;
}

function write(entries: SavedWork[]): void {
  try {
    window.localStorage.setItem(KEY, JSON.stringify([...entries].sort(byNewestEdit).slice(0, LIMIT)));
  } catch {
  }
}

function byNewestEdit(a: SavedWork, b: SavedWork): number {
  return b.updatedAt - a.updatedAt;
}

function isSavedWork(value: unknown): value is SavedWork {
  if (value === null || typeof value !== "object") return false;
  const entry = value as Partial<SavedWork>;

  return (
    typeof entry.editToken === "string" &&
    typeof entry.shareToken === "string" &&
    typeof entry.name === "string" &&
    (entry.challengeSlug === null || typeof entry.challengeSlug === "string") &&
    typeof entry.createdAt === "number" &&
    typeof entry.updatedAt === "number" &&
    (entry.score === undefined || entry.score === null || typeof entry.score === "number")
  );
}
