import { useSyncExternalStore } from "react";
import { appearance, chooseAppearance, subscribeAppearance } from "../domain/appearance";
import type { Appearance } from "../domain/editorPrefs";

/** La preferencia de aspecto, la misma para la barra de la web y para la del editor. */
export function useAppearance(): [Appearance, (next: Appearance) => void] {
  const value = useSyncExternalStore(subscribeAppearance, appearance, defaultAppearance);
  return [value, chooseAppearance];
}

const defaultAppearance = (): Appearance => "default";
