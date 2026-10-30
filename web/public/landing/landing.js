// The landing page's only script; the page works without it (the header stays frosted, the row scrolls by hand).
document.addEventListener("DOMContentLoaded", () => {
  // The header: clear on the hero at the top, frosted below it; it slides away while scrolling down and comes back
  // on the way up (never while its menu is open or the keyboard is on one of its links).
  const top = document.querySelector(".top");
  const menu = top?.querySelector(".menu");
  if (top) {
    let last = window.scrollY;
    const place = () => {
      const y = window.scrollY;
      top.classList.toggle("at-top", y < 8);
      if (Math.abs(y - last) < 6) return;
      const down = y > last && y > top.offsetHeight;
      top.classList.toggle("hide", down && !menu?.open && !top.querySelector(":focus-visible"));
      last = y;
    };
    window.addEventListener("scroll", place, { passive: true });
    top.addEventListener("focusin", () => top.classList.remove("hide"));
    // a link in the small screens' menu closes it
    menu?.addEventListener("click", (e) => { if (e.target.closest("a")) menu.open = false; });
    place();
    requestAnimationFrame(() => requestAnimationFrame(() => top.classList.add("ready")));
  }

  // The screens' arrows and dots.
  const track = document.querySelector(".track");
  const dots = [...document.querySelectorAll(".dots span")];
  if (!track) return;
  const width = () => (track.querySelector(".shot")?.getBoundingClientRect().width ?? 300) + 18;
  for (const b of document.querySelectorAll(".arrow")) {
    // right to left: «next» moves toward the end, which is to the left
    b.addEventListener("click", () => track.scrollBy({ left: -Number(b.dataset.step) * width(), behavior: "smooth" }));
  }
  const mark = () => {
    const max = track.scrollWidth - track.clientWidth;
    const at = max > 0 ? Math.round((Math.abs(track.scrollLeft) / max) * (dots.length - 1)) : 0;
    dots.forEach((d, i) => d.classList.toggle("on", i === at));
  };
  track.addEventListener("scroll", mark, { passive: true });
  mark();
});
