import { useCallback, useSyncExternalStore } from "react";

export const NARROW_VIEWPORT = "(max-width: 1023px)";

export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);
      return () => list.removeEventListener("change", onChange);
    },
    [query],
  );

  return useSyncExternalStore(subscribe, () => window.matchMedia(query).matches);
}

export function useNarrowViewport(): boolean {
  return useMediaQuery(NARROW_VIEWPORT);
}
