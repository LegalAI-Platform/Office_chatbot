import { afterEach, describe, expect, it, vi } from "vitest";
import { currentTheme, setTheme } from "./theme";

// the DOM types say every browser has it; jsdom does not
const doc = document as unknown as Record<string, unknown>;

afterEach(() => {
  delete doc.startViewTransition;
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  vi.restoreAllMocks();
});

describe("the theme switch", () => {
  it("grows the new look as a circle from the pressed button", async () => {
    const ready = Promise.resolve();
    doc.startViewTransition = vi.fn((update: () => void) => { update(); return { ready }; });
    const animate = vi.fn();
    document.documentElement.animate = animate;
    const button = document.createElement("button");
    button.getBoundingClientRect = () => ({ left: 100, top: 40, width: 60, height: 20 }) as DOMRect;

    setTheme("dark", button);
    await ready;
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(localStorage.getItem("hazem-theme")).toBe("dark");
    const [frames, options] = animate.mock.calls[0]!;
    expect(frames.clipPath[0]).toBe("circle(0px at 130px 50px)");
    expect(options.pseudoElement).toBe("::view-transition-new(root)");
  });

  it("is instant where the browser cannot draw it, and «زي الجهاز» forgets the pick", () => {
    setTheme("dark", document.createElement("button"));
    expect(currentTheme()).toBe("dark");
    setTheme("system");
    expect(localStorage.getItem("hazem-theme")).toBeNull();
    expect(document.documentElement.dataset.theme).toBeUndefined();
  });

  it("does not animate when the look stays the same", () => {
    setTheme("light");
    const start = vi.fn();
    doc.startViewTransition = start;
    setTheme("light", document.createElement("button"));
    expect(start).not.toHaveBeenCalled();
  });
});
