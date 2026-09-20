import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// 5174 a propósito: el frontend actual usa 5173 y los dos tienen que poder correr juntos.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5174,
    headers: { "Referrer-Policy": "same-origin" },
    proxy: {
      "/api": { target: "http://localhost:5012", changeOrigin: true },
    },
  },
  build: {
    rollupOptions: {
      output: {
        // Por ruta de fichero y no por nombre de paquete. Con la forma de objeto, rollup metía
        // React **dentro** del trozo de xyflow —es dependencia suya—, así que la portada tenía que
        // descargar el motor del lienzo para poder arrancar: salía de la primera carga en los
        // papeles y no en la red. Nombrar `react` antes tampoco bastaba: el tiempo de ejecución de
        // JSX entra por un fichero (`react/cjs/react-jsx-runtime…`) que ese nombre no casa.
        //
        // El orden de las tres preguntas es la regla: `@xyflow/react` lleva `react` en la ruta y
        // se decide primero, o caería en el trozo equivocado.
        manualChunks(id: string) {
          if (!id.includes("node_modules")) return undefined;
          if (id.includes("@xyflow")) return "xyflow";
          if (id.includes("opentype.js")) return "opentype";
          if (/node_modules\/(react|react-dom|react-router|react-router-dom|scheduler)\//.test(id)) return "react";
          return undefined;
        },
      },
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    // El catálogo inglés viaja aparte en la aplicación; aquí se precarga para que
    // `createTranslator("en")` siga siendo síncrono en las pruebas. Ver src/i18n/testCatalogs.ts.
    setupFiles: ["./src/i18n/testCatalogs.ts"],
  },
});
