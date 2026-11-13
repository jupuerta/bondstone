import { useCallback, useEffect, useMemo, useState } from "react";
import { NotFoundError, getArchitectureByEditToken } from "../api/architectureClient";
import { forgetWork, restoreWork, savedWork, type SavedWork } from "../domain/savedWork";

export interface SavedWorkList {
  work: SavedWork[];
  forgotten: SavedWork | null;
  forget: (entry: SavedWork) => void;
  undoForget: () => void;
}

// El llavero vive en este navegador y no sabe nada del servidor: una arquitectura borrada desde
// otro sitio —o desde aquí, si algo se torció por el camino— seguiría figurando en la lista para
// siempre, porque nada la contrasta. Aquí se contrastan las que se ven, y solo un 404 retira la
// entrada: un fallo de red deja la lista como está, que es lo contrario de perder la llave.
export function useSavedWork(shown: number): SavedWorkList {
  const [work, setWork] = useState<SavedWork[]>(() => savedWork());
  const [forgotten, setForgotten] = useState<SavedWork | null>(null);

  const visible = useMemo(() => work.slice(0, shown), [work, shown]);
  const visibleTokens = visible.map((entry) => entry.editToken).join(" ");

  useEffect(() => {
    if (visibleTokens === "") return;
    let cancelled = false;

    void Promise.all(
      visibleTokens.split(" ").map((editToken) =>
        getArchitectureByEditToken(editToken).then(
          () => null,
          (error: unknown) => (error instanceof NotFoundError ? editToken : null),
        ),
      ),
    ).then((checked) => {
      const missing = checked.filter((editToken): editToken is string => editToken !== null);
      if (cancelled || missing.length === 0) return;

      for (const editToken of missing) forgetWork(editToken);
      setWork(savedWork());
    });

    return () => {
      cancelled = true;
    };
  }, [visibleTokens]);

  const forget = useCallback((entry: SavedWork) => {
    forgetWork(entry.editToken);
    setWork(savedWork());
    setForgotten(entry);
  }, []);

  const undoForget = useCallback(() => {
    setForgotten((entry) => {
      if (entry !== null) {
        restoreWork(entry);
        setWork(savedWork());
      }
      return null;
    });
  }, []);

  return { work: visible, forgotten, forget, undoForget };
}
