import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The panel talks to /api/panel only. In `npm run dev` the requests go to the web container
// (127.0.0.1:8080), whose nginx forwards /api/panel and nothing else to the API.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: { "/api/panel": { target: "http://127.0.0.1:8080", changeOrigin: false } },
  },
  build: {
    // No inline scripts or data: fonts, so the Content-Security-Policy can stay "self" only.
    assetsInlineLimit: 0,
    sourcemap: false,
  },
  test: {
    environment: "jsdom",
    globals: true,
    css: false,
  },
});
