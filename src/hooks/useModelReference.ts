import { useEffect, useState } from "react";
import { getModel, type ModelReference } from "../api/modelClient";

let cached: ModelReference | null = null;
let inFlight: Promise<ModelReference> | null = null;

export function useModelReference(): ModelReference | null {
  const [model, setModel] = useState<ModelReference | null>(cached);

  useEffect(() => {
    if (cached !== null) return;

    let live = true;

    inFlight ??= getModel();
    inFlight
      .then((loaded) => {
        cached = loaded;
        if (live) setModel(loaded);
      })
      .catch(() => {
        inFlight = null;
      });

    return () => {
      live = false;
    };
  }, []);

  return model;
}
