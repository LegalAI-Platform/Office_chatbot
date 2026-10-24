// The bundle's size, printed after every build so a big jump is seen. No limit: the owner removed the
// budget on 2026-10-24 (TASK 09), so the build never fails on size. Fonts are not counted: they are cached
// and loaded only for the glyphs in use.
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { gzipSync } from "node:zlib";

const dir = join(import.meta.dirname, "..", "dist", "assets");
const totals = { js: 0, css: 0 };
for (const name of readdirSync(dir)) {
  const kind = name.endsWith(".js") ? "js" : name.endsWith(".css") ? "css" : null;
  if (kind) totals[kind] += gzipSync(readFileSync(join(dir, name))).length / 1024;
}
for (const kind of ["js", "css"]) {
  console.log(`SIZE  ${kind}: ${totals[kind].toFixed(1)} KB gzip (no limit)`);
}
