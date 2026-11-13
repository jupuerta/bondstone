/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL?: string;
  /** Enlace de solo lectura de «la tienda» rota: la demostración de la portada. */
  readonly VITE_DEMO_BEFORE_TOKEN?: string;
  /** La misma tienda, arreglada: el «después» del conmutador. */
  readonly VITE_DEMO_AFTER_TOKEN?: string;
  /** El nombre del nodo que se tumba en la demostración (se busca por nombre, no por id). */
  readonly VITE_DEMO_FAULT_NODE?: string;
}
