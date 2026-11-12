// Where a bubble of the panel's tour goes (v1.102): plain geometry on the screen's pixels, for app/Tour.tsx.

export interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

export interface Size {
  width: number;
  height: number;
}

/** Physical sides (the screen's left and right, whatever the language): where the bubble is, from what it points at. */
export type Side = "below" | "above" | "left" | "right" | "free";

export interface Placement {
  left: number;
  top: number;
  side: Side;
  /** Where the arrow sits along the bubble's edge that faces the target (px from that edge's start); none when free. */
  arrow: number;
}

/** Room kept from the screen's edges, the gap between what is lit and its bubble, and the arrow's end room. */
export const EDGE = 12;
export const GAP = 14;
const ARROW_END = 24;

/**
 * Where a bubble of `bubble` size goes on a `view` screen for the lit `hole` (null: the middle of the screen). Beside a
 * side-bar link it goes toward the page (`sideBar`), else under or over it, else beside it; when nothing fits, it goes
 * over the half of the screen away from the target, without its arrow.
 */
export function place(hole: Box | null, bubble: Size, view: Size, sideBar = false): Placement {
  // a phone: the bubble is the screen's width less 2rem, so it sits in the middle (1rem each side)
  const x = (v: number) => view.width - bubble.width <= 32
    ? (view.width - bubble.width) / 2
    : Math.min(Math.max(v, EDGE), view.width - bubble.width - EDGE);
  const y = (v: number) => Math.min(Math.max(v, EDGE), Math.max(EDGE, view.height - bubble.height - EDGE));
  if (!hole) return { left: x((view.width - bubble.width) / 2), top: y((view.height - bubble.height) / 2), side: "free", arrow: 0 };

  const cx = hole.left + hole.width / 2;
  const cy = hole.top + hole.height / 2;
  const along = (center: number, start: number, length: number) =>
    Math.min(Math.max(center - start, ARROW_END), Math.max(ARROW_END, length - ARROW_END));
  const fits: Record<Exclude<Side, "free">, boolean> = {
    below: hole.top + hole.height + GAP + bubble.height <= view.height - EDGE,
    above: hole.top - GAP - bubble.height >= EDGE,
    left: hole.left - GAP - bubble.width >= EDGE,
    right: hole.left + hole.width + GAP + bubble.width <= view.width - EDGE,
  };
  const order: Exclude<Side, "free">[] = sideBar ? ["left", "right", "below", "above"] : ["below", "above", "left", "right"];
  for (const side of order) {
    if (!fits[side]) continue;
    if (side === "below" || side === "above") {
      const left = x(cx - bubble.width / 2);
      const top = side === "below" ? hole.top + hole.height + GAP : hole.top - GAP - bubble.height;
      return { left, top, side, arrow: along(cx, left, bubble.width) };
    }
    const top = y(cy - bubble.height / 2);
    const left = side === "left" ? hole.left - GAP - bubble.width : hole.left + hole.width + GAP;
    return { left, top, side, arrow: along(cy, top, bubble.height) };
  }
  return { left: x(cx - bubble.width / 2), top: cy < view.height / 2 ? y(view.height) : EDGE, side: "free", arrow: 0 };
}

/** The lit area around a target: a little room around it, kept on the screen. */
export function holeAround(rect: Box, view: Size, pad = 6): Box {
  const left = Math.max(rect.left - pad, 2);
  const top = Math.max(rect.top - pad, 2);
  const right = Math.min(rect.left + rect.width + pad, view.width - 2);
  const bottom = Math.min(rect.top + rect.height + pad, view.height - 2);
  return { left, top, width: Math.max(right - left, 0), height: Math.max(bottom - top, 0) };
}

/** The dim with its hole: the whole screen, and a rounded rectangle cut out of it (drawn even-odd). */
export function dimPath(view: Size, hole: Box | null, radius = 16): string {
  const screen = `M0 0H${view.width}V${view.height}H0Z`;
  if (!hole || hole.width <= 0 || hole.height <= 0) return screen;
  const r = Math.min(radius, hole.width / 2, hole.height / 2);
  const { left: l, top: t, width: w, height: h } = hole;
  return `${screen}M${l + r} ${t}H${l + w - r}A${r} ${r} 0 0 1 ${l + w} ${t + r}V${t + h - r}A${r} ${r} 0 0 1 ${l + w - r} ${t + h}`
    + `H${l + r}A${r} ${r} 0 0 1 ${l} ${t + h - r}V${t + r}A${r} ${r} 0 0 1 ${l + r} ${t}Z`;
}
