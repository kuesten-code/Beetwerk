import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

const backend = process.env.BEETWERK_BACKEND ?? "http://localhost:5080";

export default defineConfig({
  plugins: [react()],
  build: {
    // Das Backend liefert das Frontend aus (hinter der Anmeldung), daher direkt in dessen wwwroot.
    outDir: "../src/Kuestencode.Beetwerk.Api/wwwroot",
    emptyOutDir: true,
    // MapLibre + Terra Draw machen den Großteil aus; durch Hash-Dateinamen wird das Bundle dauerhaft gecacht.
    chunkSizeWarningLimit: 2000,
  },
  server: {
    proxy: {
      "/api": backend,
      "/login": backend,
      "/logout": backend,
      "/health": backend,
      "/tiles": backend,
    },
  },
  worker: {
    format: "es",
  },
  test: {
    environment: "node",
  },
});
