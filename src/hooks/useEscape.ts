import { useEffect, useRef } from "react";

export function useEscape(onEscape: () => void): void {
  const latest = useRef(onEscape);
  latest.current = onEscape;

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") latest.current();
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);
}
