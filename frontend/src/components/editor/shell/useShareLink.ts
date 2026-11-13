import { useCallback, useEffect, useRef, useState } from "react";

export function useShareLink(url: string): { copy: () => void; copied: boolean } {
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | null>(null);

  useEffect(() => () => {
    if (timer.current !== null) window.clearTimeout(timer.current);
  }, []);

  const copy = useCallback(() => {
    const done = () => {
      setCopied(true);
      if (timer.current !== null) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setCopied(false), 2000);
    };
    try {
      navigator.clipboard.writeText(url).then(done, () => undefined);
    } catch {
      return;
    }
  }, [url]);

  return { copy, copied };
}
