// Light or dark, set on <html> before the page is drawn: the visitor's own choice (the header's moon and sun, kept in
// this browser), else the device's. A click turns the page in a circle of the new colours that opens from the button
// (a view transition; at once for those who ask for less motion, and in browsers without one).
(() => {
  const root = document.documentElement;
  const device = window.matchMedia("(prefers-color-scheme: dark)");
  const still = window.matchMedia("(prefers-reduced-motion: reduce)");
  const meta = document.querySelector('meta[name="theme-color"]');
  let pick = null;
  try { pick = localStorage.getItem("theme"); } catch {}

  const paint = () => {
    const dark = pick === "dark" || (pick !== "light" && device.matches);
    root.dataset.theme = dark ? "dark" : "light";
    meta?.setAttribute("content", dark ? "#14110d" : "#f7f2ea");
    document.querySelector(".theme")?.setAttribute("aria-pressed", String(dark));
  };
  paint();
  device.addEventListener("change", paint);
  document.addEventListener("DOMContentLoaded", paint);

  document.addEventListener("click", (e) => {
    const button = e.target.closest?.(".theme");
    if (!button) return;
    pick = root.dataset.theme === "dark" ? "light" : "dark";
    try { localStorage.setItem("theme", pick); } catch {}
    if (!document.startViewTransition || still.matches) return paint();
    const r = button.getBoundingClientRect();
    const x = r.left + r.width / 2;
    const y = r.top + r.height / 2;
    const end = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
    document.startViewTransition(paint).ready.then(() => {
      root.animate(
        { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${end}px at ${x}px ${y}px)`] },
        { duration: 650, easing: "cubic-bezier(.4, 0, .2, 1)", pseudoElement: "::view-transition-new(root)" },
      );
    }).catch(() => {});
  });
})();
