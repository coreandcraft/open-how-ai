import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwind from "@tailwindcss/vite";
const apiUrl = process.env.JOURNEY_API_URL || "http://127.0.0.1:5080";
export default defineConfig({
  plugins: [react(), tailwind()],
  resolve: {
    extensions: [".mjs", ".mts", ".ts", ".tsx", ".jsx", ".js", ".json"],
  },
  server: {
    proxy: {
      "/mcp": apiUrl,
      "/.well-known/oauth-protected-resource": apiUrl,
      "/api": apiUrl,
      "/hubs": { target: apiUrl, ws: true },
      "/openapi": apiUrl,
      "/signin-google": apiUrl,
      "/signin-facebook": apiUrl,
    },
  },
  build: {
    outDir: "../Journey.Server/wwwroot",
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          const normalized = id.replaceAll("\\", "/");
          if (normalized.includes("/shared/i18n/locales/")) return "locales";
        },
      },
    },
  },
});
