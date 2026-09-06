import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// 5174 a propósito: el frontend actual usa 5173 y los dos tienen que poder correr juntos.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5174,
    proxy: {
      "/api": { target: "http://localhost:5012", changeOrigin: true },
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
