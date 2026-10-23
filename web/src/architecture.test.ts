// frontend-architecture's one rule, enforced by a test instead of a lint plugin (no extra
// dependency): a feature never imports another feature; shared/ never imports a feature.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import { describe, expect, it } from "vitest";

const SRC = resolve(__dirname);

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? files(path) : /\.tsx?$/.test(name) ? [path] : [];
  });
}

function imports(file: string): string[] {
  const text = readFileSync(file, "utf8");
  return [...text.matchAll(/from\s+["'](\.[^"']+)["']/g)].map((m) => resolve(file, "..", m[1]!));
}

const area = (path: string): string => {
  const parts = relative(SRC, path).split(sep);
  return parts[0] === "features" ? `features/${parts[1]}` : parts[0]!;
};

describe("module boundaries", () => {
  const all = files(SRC).filter((f) => !f.endsWith(".test.ts") && !f.endsWith(".test.tsx"));

  it("features do not import each other", () => {
    const bad = all.flatMap((f) =>
      imports(f)
        .filter((target) => area(f).startsWith("features/") && area(target).startsWith("features/") && area(target) !== area(f))
        .map((target) => `${relative(SRC, f)} -> ${relative(SRC, target)}`),
    );
    expect(bad).toEqual([]);
  });

  it("shared code does not import features or the app", () => {
    const bad = all
      .filter((f) => area(f) === "shared")
      .flatMap((f) => imports(f).filter((t) => area(t) !== "shared").map((t) => `${relative(SRC, f)} -> ${relative(SRC, t)}`));
    expect(bad).toEqual([]);
  });

  it("nothing calls fetch outside the shared API client", () => {
    const bad = all
      .filter((f) => !f.endsWith(join("shared", "api", "client.ts")))
      .filter((f) => /\bfetch\(/.test(readFileSync(f, "utf8")))
      .map((f) => relative(SRC, f));
    expect(bad).toEqual([]);
  });
});
