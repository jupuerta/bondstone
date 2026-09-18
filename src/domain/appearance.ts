import { editorPrefs, rememberEditorPrefs, type Appearance, type EditorTheme } from "./editorPrefs";

/** Lo que pinta el editor mientras nadie elige: el motor en oscuro. */
export const editorTheme = (appearance: Appearance): EditorTheme => (appearance === "light" ? "light" : "dark");

/** Lo que pinta la web mientras nadie elige: clara. */
export const webTheme = (appearance: Appearance): EditorTheme => (appearance === "dark" ? "dark" : "light");

/** Sin elegir no hay atributo que poner: cada superficie se queda con el suyo. */
export function applyAppearance(appearance: Appearance): void {
  const root = document.documentElement;
  if (appearance === "default") root.removeAttribute("data-appearance");
  else root.setAttribute("data-appearance", appearance);
}

const listeners = new Set<() => void>();
let current: Appearance | null = null;

export function appearance(): Appearance {
  if (current === null) current = editorPrefs().appearance;
  return current;
}

export function chooseAppearance(next: Appearance): void {
  if (next === appearance()) return;
  current = next;
  rememberEditorPrefs({ appearance: next });
  applyAppearance(next);
  for (const listener of [...listeners]) listener();
}

export function subscribeAppearance(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
