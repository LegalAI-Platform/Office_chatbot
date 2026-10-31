import { defineConfig } from "vite";

// The landing page's motion (landing-fx: GSAP and Three.js) as plain files for /landing/fx/, built after the panel
// into its dist; the landing itself is plain files in public/landing, copied as they are. fx.js is what the page
// loads; the Three.js part is a file of its own, fetched with the first click. Same CSP as the panel: no inline code.
export default defineConfig({
  base: "./",
  publicDir: false,
  build: {
    outDir: "dist/landing/fx",
    emptyOutDir: true,
    assetsInlineLimit: 0,
    sourcemap: false,
    chunkSizeWarningLimit: 600, // the Three.js part (about 545 kB, 136 kB gzip), fetched once the page has settled
    modulePreload: { polyfill: false },
    rolldownOptions: {
      input: "landing-fx/fx.js",
      output: { entryFileNames: "fx.js", chunkFileNames: "[name]-[hash].js" },
    },
  },
});
