// Bundle budget (frontend-architecture): the build fails when the gzipped JS or CSS grows past
// the limit. Fonts are not counted: they are cached and loaded only for the glyphs in use.
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { gzipSync } from "node:zlib";

const BUDGET_KB = { js: 150, css: 15 };
const dir = join(import.meta.dirname, "..", "dist", "assets");
const totals = { js: 0, css: 0 };
for (const name of readdirSync(dir)) {
  const kind = name.endsWith(".js") ? "js" : name.endsWith(".css") ? "css" : null;
  if (kind) totals[kind] += gzipSync(readFileSync(join(dir, name))).length / 1024;
}
let failed = false;
for (const kind of ["js", "css"]) {
  const ok = totals[kind] <= BUDGET_KB[kind];
  failed ||= !ok;
  console.log(`${ok ? "PASS" : "FAIL"}  ${kind}: ${totals[kind].toFixed(1)} KB gzip (budget ${BUDGET_KB[kind]} KB)`);
}
process.exit(failed ? 1 : 0);
