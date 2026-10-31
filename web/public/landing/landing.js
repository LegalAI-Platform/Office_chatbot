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

  // The screens: a ring. An arrow (or a swipe on a phone) slides the row by one screen; the one that leaves at one end
  // comes back in at the other, and the dots follow the screen that leads. Without the script the row scrolls by hand.
  const track = document.querySelector(".track");
  const dots = [...document.querySelectorAll(".dots span")];
  if (!track) return;
  const still = window.matchMedia("(prefers-reduced-motion: reduce)");
  let at = 0;
  let busy = false;
  let waiting = 0; // clicks that came while the row was moving, played after it
  track.classList.add("loop");
  // step 1 («next», the left arrow): the row moves right, the first screen leaves and comes back last, on the left
  const slide = (step) => {
    const shots = [...track.querySelectorAll(".shot:not(.ghost)")];
    if (shots.length < 2) return;
    if (busy) {
      waiting += step;
      return;
    }
    const out = step > 0 ? shots[0] : shots.at(-1);
    const was = out.getBoundingClientRect();
    const stay = step > 0 ? shots[1] : shots[0];
    const from = stay.getBoundingClientRect().left;
    if (step > 0) track.append(out);
    else track.prepend(out);
    at = (at + step + shots.length) % shots.length;
    dots.forEach((d, i) => d.classList.toggle("on", i === at));
    if (still.matches) return;

    // every screen starts where it was and slides one place; a copy of the one that left carries on out of sight
    const dx = from - stay.getBoundingClientRect().left;
    const ghost = out.cloneNode(true);
    ghost.classList.add("ghost");
    ghost.setAttribute("aria-hidden", "true");
    ghost.style.left = `${was.left - track.getBoundingClientRect().left}px`;
    ghost.style.width = `${was.width}px`;
    ghost.style.height = `${was.height}px`;
    track.append(ghost);
    busy = true;
    const time = { duration: 620, easing: "cubic-bezier(.22, .8, .24, 1)" };
    const moves = shots.map((s) => s.animate([{ transform: `translateX(${dx}px)` }, { transform: "none" }], time));
    moves.push(ghost.animate([{ transform: "none" }, { transform: `translateX(${-dx}px)` }], time));
    Promise.all(moves.map((m) => m.finished)).finally(() => {
      ghost.remove();
      busy = false;
      if (waiting) {
        const next = Math.sign(waiting);
        waiting -= next;
        slide(next);
      }
    });
  };
  for (const b of document.querySelectorAll(".arrow")) b.addEventListener("click", () => slide(Number(b.dataset.step)));
  // a swipe: the row follows the finger
  let start = null;
  track.addEventListener("pointerdown", (e) => { start = e.pointerType === "mouse" ? null : [e.clientX, e.clientY]; });
  track.addEventListener("pointercancel", () => { start = null; });
  track.addEventListener("pointerup", (e) => {
    if (!start) return;
    const [dx, dy] = [e.clientX - start[0], e.clientY - start[1]];
    start = null;
    if (Math.abs(dx) > 40 && Math.abs(dx) > Math.abs(dy)) slide(dx > 0 ? 1 : -1);
  });
});
