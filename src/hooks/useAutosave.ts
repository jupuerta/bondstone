import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, ConflictError, updateArchitecture } from "../api/architectureClient";
import type { UpdateArchitectureRequest, UpdateArchitectureResult } from "../types/architecture";
import { useT } from "../i18n";

const AUTOSAVE_DEBOUNCE_MS = 1000;

export type SaveStatus = "idle" | "pending" | "saving" | "saved" | "error" | "conflict";

interface UseAutosaveOptions {
  editToken: string;
  enabled: boolean;
  getSnapshot: () => UpdateArchitectureRequest;
  onSaved: (result: UpdateArchitectureResult) => void;
  onConflict: (currentVersion: number) => void;
}

interface UseAutosaveResult {
  status: SaveStatus;
  error: string | null;
  scheduleSave: () => void;
  retryNow: () => void;
}

export function useAutosave({ editToken, enabled, getSnapshot, onSaved, onConflict }: UseAutosaveOptions): UseAutosaveResult {
  const t = useT();
  const [status, setStatus] = useState<SaveStatus>("idle");
  const [error, setError] = useState<string | null>(null);

  const timerRef = useRef<number | undefined>(undefined);
  const inFlightRef = useRef(false);
  const queuedRef = useRef(false);
  const dirtyRef = useRef(false);
  const conflictedRef = useRef(false);

  const flush = useCallback((options?: { keepalive?: boolean }) => {
    if (conflictedRef.current) return;

    if (inFlightRef.current) {
      queuedRef.current = true;
      return;
    }

    inFlightRef.current = true;
    dirtyRef.current = false;
    setStatus("saving");
    setError(null);

    updateArchitecture(editToken, getSnapshot(), options)
      .then((result) => {
        setStatus("saved");
        onSaved(result);
      })
      .catch((err: unknown) => {
        if (err instanceof ConflictError) {
          conflictedRef.current = true;
          setStatus("conflict");
          onConflict(err.currentVersion);
          return;
        }

        dirtyRef.current = true;
        setStatus("error");
        setError(err instanceof ApiError ? t(err.key, err.args) : t("ui.error.save"));
      })
      .finally(() => {
        inFlightRef.current = false;

        if (queuedRef.current) {
          queuedRef.current = false;
          flush();
        }
      });
  }, [editToken, getSnapshot, onSaved, onConflict, t]);

  const scheduleSave = useCallback(() => {
    if (!enabled || conflictedRef.current) return;

    dirtyRef.current = true;
    setStatus((current) => (current === "saving" ? current : "pending"));
    window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => flush(), AUTOSAVE_DEBOUNCE_MS);
  }, [enabled, flush]);

  const retryNow = useCallback(() => {
    window.clearTimeout(timerRef.current);
    flush();
  }, [flush]);

  useEffect(() => {
    const flushIfDirty = () => {
      if (!dirtyRef.current || conflictedRef.current) return;

      window.clearTimeout(timerRef.current);
      flush({ keepalive: true });
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === "hidden") flushIfDirty();
    };

    window.addEventListener("beforeunload", flushIfDirty);
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      window.removeEventListener("beforeunload", flushIfDirty);
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [flush]);

  useEffect(() => () => window.clearTimeout(timerRef.current), []);

  return { status, error, scheduleSave, retryNow };
}
