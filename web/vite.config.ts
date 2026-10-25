import { defineConfig } from "vitest/config";
import type { Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The panel talks to /api/panel only. In `npm run dev` the requests go to the web container
// (127.0.0.1:8080), whose nginx forwards /api/panel and nothing else to the API.
/**
 * The two faces nearly every screen starts with (Arabic regular and semibold) are asked for together with the
 * code instead of after the CSS has been read: text appears in its own font sooner, without a swap.
 */
function preloadFonts(): Plugin {
  return {
    name: "hazem-preload-fonts",
    apply: "build",
    transformIndexHtml: {
      order: "post",
      handler(_html, ctx) {
        const files = Object.keys(ctx.bundle ?? {}).filter((f) => /ibm-plex-sans-arabic-arabic-(400|600)-normal-[^/]*\.woff2$/.test(f));
        return files.map((f) => ({ tag: "link", injectTo: "head" as const, attrs: { rel: "preload", as: "font", type: "font/woff2", href: `/${f}`, crossorigin: "" } }));
      },
    },
  };
}

export default defineConfig({
  plugins: [react(), tailwindcss(), preloadFonts()],
  server: {
    proxy: { "/api/panel": { target: "http://127.0.0.1:8080", changeOrigin: false } },
  },
  build: {
    // No inline scripts or data: fonts, so the Content-Security-Policy can stay "self" only.
    assetsInlineLimit: 0,
    sourcemap: false,
    rolldownOptions: {
      output: {
        // React, the router and the query cache change far less often than the panel: their own file stays
        // cached across releases, and each page is its own file (app/lazyPages.tsx).
        advancedChunks: {
          groups: [{ name: "vendor", test: /node_modules[\\/](react|react-dom|scheduler|react-router|@tanstack)[\\/]/ }],
        },
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    css: false,
    setupFiles: ["src/test-setup.ts"],
  },
});
