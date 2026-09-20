import { useEffect } from "react";

const DEFAULT_TITLE = "Bondstone";

export function usePageTitle(title: string | null) {
  useEffect(() => {
    if (title === null || title.trim() === "") return;

    document.title = `${title} — ${DEFAULT_TITLE}`;
    return () => {
      document.title = DEFAULT_TITLE;
    };
  }, [title]);
}
