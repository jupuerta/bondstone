import { useCallback, useEffect, useRef, useState } from "react";
import type { NodeType } from "../../../types/architecture";


export const APPEAR_MS = 400;

export const GRACE_MS = 300;

export const CLOSE_MS = 60;

export interface ComponentHelpHover {
  type: NodeType | null;
  anchorTop: number;
  enter: (type: NodeType, anchorTop: number) => void;
  focus: (type: NodeType, anchorTop: number) => void;
  leave: () => void;
  keep: () => void;
  leaveCard: () => void;
  close: () => void;
}

export function useComponentHelp(): ComponentHelpHover {
  const [shown, setShown] = useState<{ type: NodeType; anchorTop: number } | null>(null);
  const timer = useRef<number | undefined>(undefined);

  const clear = useCallback(() => window.clearTimeout(timer.current), []);
  useEffect(() => clear, [clear]);

  const close = useCallback(() => {
    clear();
    setShown(null);
  }, [clear]);

  const after = useCallback(
    (ms: number, next: { type: NodeType; anchorTop: number } | null) => {
      clear();
      timer.current = window.setTimeout(() => setShown(next), ms);
    },
    [clear],
  );

  useEffect(() => {
    if (shown === null) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [shown, close]);

  return {
    type: shown?.type ?? null,
    anchorTop: shown?.anchorTop ?? 0,
    enter: useCallback((type, anchorTop) => after(APPEAR_MS, { type, anchorTop }), [after]),
    focus: useCallback((type, anchorTop) => after(0, { type, anchorTop }), [after]),
    leave: useCallback(() => after(GRACE_MS, null), [after]),
    keep: clear,
    leaveCard: useCallback(() => after(CLOSE_MS, null), [after]),
    close,
  };
}
