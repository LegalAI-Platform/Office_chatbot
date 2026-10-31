// The landing page's motion, built by vite.landing.config.ts into /landing/fx/fx.js (the page works without it).
// Every button answers the hand (GSAP): pulled a little toward the pointer, it gives under the press and springs back,
// and light spreads from the touch; the click throws a burst of gold (burst.js, Three.js, a file of its own), fetched
// and made ready once the page has settled, so the first click finds it waiting. Nothing moves for those whose system
// asks for less motion.
import { gsap } from "gsap";

const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const fine = window.matchMedia("(hover: hover) and (pointer: fine)").matches;
const SPRING = "elastic.out(1.15, 0.32)";
let layer; // the gold burst (burst.js)
const ready = () => (layer ??= import("./burst.js").then((m) => m.makeBurst()).catch(() => null));

if (!still) {
  document.documentElement.classList.add("fx");
  for (const el of document.querySelectorAll(".btn, .arrow")) wire(el, true);
  for (const el of document.querySelectorAll(".card a, .menu summary")) wire(el, false);
  const later = window.requestIdleCallback ?? ((f) => setTimeout(f, 1500));
  window.addEventListener("load", () => later(ready, { timeout: 4000 }), { once: true });
}

function wire(el, big) {
  if (fine && big) {
    const x = gsap.quickTo(el, "x", { duration: 0.6, ease: "elastic.out(1, 0.4)" });
    const y = gsap.quickTo(el, "y", { duration: 0.6, ease: "elastic.out(1, 0.4)" });
    el.addEventListener("pointermove", (e) => {
      const r = el.getBoundingClientRect();
      x((e.clientX - r.left - r.width / 2) * 0.16);
      y((e.clientY - r.top - r.height / 2) * 0.28);
    });
    el.addEventListener("pointerleave", () => { x(0); y(0); });
  }
  el.addEventListener("pointerdown", (e) => {
    gsap.to(el, { scale: 0.93, duration: 0.14, ease: "power3.out", overwrite: "auto" });
    if (big) ripple(el, e.clientX, e.clientY);
  });
  const release = () => gsap.to(el, { scale: 1, duration: 0.9, ease: SPRING, overwrite: "auto" });
  for (const type of ["pointerup", "pointerleave", "pointercancel"]) el.addEventListener(type, release);

  el.addEventListener("click", (e) => {
    const r = el.getBoundingClientRect();
    const keys = e.detail === 0; // Enter or Space: from the middle of the button
    const px = keys ? r.left + r.width / 2 : e.clientX;
    const py = keys ? r.top + r.height / 2 : e.clientY;
    if (keys) {
      gsap.fromTo(el, { scale: 0.93 }, { scale: 1, duration: 0.9, ease: SPRING, overwrite: "auto" });
      if (big) ripple(el, px, py);
    }
    burst(px, py, el.matches(".btn-gold, .btn-dark") ? 1 : big ? 0.7 : 0.45);
    // a link to another page waits a moment so the burst is seen (not with a modifier key: a new tab opens at once)
    const href = el.getAttribute("href");
    if (href && !href.startsWith("#") && !e.defaultPrevented && !(e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) && el.target !== "_blank") {
      e.preventDefault();
      setTimeout(() => window.location.assign(el.href), 380);
    }
  });
}

// light spreading from the touch, under the button's text
function ripple(el, x, y) {
  const r = el.getBoundingClientRect();
  const d = Math.hypot(r.width, r.height) * 2;
  const s = document.createElement("span");
  s.className = "fx-ripple";
  s.style.width = s.style.height = `${d}px`;
  s.style.left = `${x - r.left - d / 2}px`;
  s.style.top = `${y - r.top - d / 2}px`;
  el.append(s);
  gsap.fromTo(s, { scale: 0, opacity: 0.6 }, { scale: 1, opacity: 0, duration: 0.8, ease: "power2.out", onComplete: () => s.remove() });
}

function burst(x, y, power) {
  ready().then((l) => l?.fire(x, y, power));
}
