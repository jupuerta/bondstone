import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// 5174 a propósito: el frontend actual usa 5173 y los dos tienen que poder correr juntos.
// El editor antiguo (puerto 8080) queda detrás de /manual* y /assets/, igual que en nginx.conf,
// hasta la rebanada 6. /e/ ya es de este frontend.
const LEGACY = "http://localhost:8080";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5174,
    headers: { "Referrer-Policy": "same-origin" },
    proxy: {
      "/api": { target: "http://localhost:5012", changeOrigin: true },
      "/manual": { target: LEGACY, changeOrigin: true },
      "/assets/": { target: LEGACY, changeOrigin: true },
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks: { xyflow: ["@xyflow/react"] },
      },
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
  },
});
