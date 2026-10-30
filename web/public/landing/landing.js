// The screens' arrows and dots (the landing page's only script; the page works without it, the row scrolls by hand).
document.addEventListener("DOMContentLoaded", () => {
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
