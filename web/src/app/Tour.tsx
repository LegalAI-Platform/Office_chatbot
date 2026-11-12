import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useLocation, useNavigate } from "react-router";
import { api } from "../shared/api/client";
import { meKey, useMe } from "../shared/api/session";
import type { Me } from "../shared/api/types";
import { Icon } from "../shared/ui/Icon";
import { OwnerImg } from "../shared/ui/Owner";
import { dimPath, holeAround, place, type Box, type Placement } from "./tourPlace";
import { tourSteps, type TourStep } from "./tourSteps";

type Run = { steps: TourStep[]; at: number; first: boolean };
/** `target`: the data-tour name lit now (the words may say where it is: «المزيد» on a phone). */
type View = { hole: Box | null; at: Placement | null; wide: boolean; target: string | null; width: number; height: number };

const wideScreen = () => typeof matchMedia === "function" && matchMedia("(min-width: 640px)").matches;
const nextFrame = (f: () => void) => (typeof requestAnimationFrame === "function" ? requestAnimationFrame(f) : window.setTimeout(f, 16));
const cancelFrame = (id: number) => (typeof cancelAnimationFrame === "function" ? cancelAnimationFrame(id) : window.clearTimeout(id));

/** The first element with one of these data-tour names that is on the screen (the hidden side bar or tab bar has no size). */
function findTarget(names: string[]): { el: HTMLElement; name: string } | null {
  for (const name of names) {
    for (const el of document.querySelectorAll<HTMLElement>(`[data-tour="${name}"]`)) {
      const r = el.getBoundingClientRect();
      if (r.width > 0 && r.height > 0) return { el, name };
    }
  }
  return null;
}

/**
 * The panel's tour (v1.102, app/tourSteps.ts and tourPlace.ts): bubbles over the real panel, the rest of the screen dimmed. It opens on the home
 * page by itself once for an account that has not seen it (/auth/me tour_seen false), and from «اعرض الجولة تاني»
 * (TOUR_REPLAY) any time. «تخطّي», Esc or the last step's «تمام» ends it, and the first time that is saved on the server for
 * the account (every device knows). The page under it cannot be clicked or reached (inert) while it is open.
 */
export function Tour() {
  const { data: me } = useMe();
  const location = useLocation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const seen = useMutation({ mutationFn: () => api<void>("POST", "/account/tour-seen") });
  const [run, setRun] = useState<Run | null>(null);
  const [view, setView] = useState<View>({ hole: null, at: null, wide: false, target: null, width: 0, height: 0 });
  const [tick, setTick] = useState(0);
  const shownAlone = useRef(false); // on its own at most once per page load
  const scrolledFor = useRef<string | null>(null);
  const bubble = useRef<HTMLDivElement>(null);
  const primary = useRef<HTMLButtonElement>(null);
  const id = useId();
  const home = location.pathname === "/";
  const replay = home && new URLSearchParams(location.search).has("tour");
  const open = run !== null;
  const step = run?.steps[run.at];

  // starts: asked for (the address says so, then forgets it), or by itself shortly after the home page shows
  useEffect(() => {
    if (!me || run) return;
    if (replay) {
      void navigate("/", { replace: true });
      setRun({ steps: tourSteps(me), at: 0, first: me.tour_seen === false });
      return;
    }
    if (!home || me.tour_seen !== false || shownAlone.current) return;
    const t = window.setTimeout(() => {
      shownAlone.current = true;
      setRun({ steps: tourSteps(me), at: 0, first: true });
    }, 700);
    return () => window.clearTimeout(t);
  }, [me, run, replay, home, navigate]);

  // another page (the browser's back button): it closes, and comes back next time if it was never ended
  useEffect(() => {
    if (!home) setRun(null);
  }, [home]);

  // the page under it: no clicks, no focus, not read out; afterwards the focus, and the page where the tour scrolled it
  // (a phone), go back where they were
  useEffect(() => {
    if (!open) return;
    const root = document.getElementById("root");
    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const scrolled = window.scrollY;
    root?.setAttribute("inert", "");
    return () => {
      root?.removeAttribute("inert");
      before?.focus({ preventScroll: true });
      if (window.scrollY !== scrolled) window.scrollTo({ top: scrolled });
    };
  }, [open]);

  // measured again when the screen turns, resizes or scrolls, and when the bubble's own size changes
  useEffect(() => {
    if (!open) return;
    let frame = 0;
    const again = () => {
      cancelFrame(frame);
      frame = nextFrame(() => setTick((t) => t + 1));
    };
    window.addEventListener("resize", again);
    window.addEventListener("scroll", again, true);
    const watch = typeof ResizeObserver === "function" ? new ResizeObserver(again) : null;
    if (bubble.current) watch?.observe(bubble.current);
    watch?.observe(document.body);
    return () => {
      cancelFrame(frame);
      window.removeEventListener("resize", again);
      window.removeEventListener("scroll", again, true);
      watch?.disconnect();
    };
  }, [open]);

  // where it all goes; measured again once the words changed with what is lit or the screen (their size moves the bubble)
  useLayoutEffect(() => {
    if (!step) return;
    const screen = { width: window.innerWidth, height: window.innerHeight };
    const found = findTarget(step.targets);
    const target = found?.el;
    let rect = target?.getBoundingClientRect();
    // off the screen (a long page on a phone): brought to the middle once, then left where the person scrolls it
    if (target && rect && scrolledFor.current !== step.id && (rect.top < 0 || rect.bottom > screen.height)) {
      scrolledFor.current = step.id;
      target.scrollIntoView?.({ block: "center" });
      rect = target.getBoundingClientRect();
    }
    const hole = target && rect ? holeAround(rect, screen) : null;
    const size = bubble.current?.getBoundingClientRect();
    const at = place(hole, { width: size?.width ?? 0, height: size?.height ?? 0 }, screen, !!target?.closest("aside"));
    setView({ hole, at, wide: wideScreen(), target: found?.name ?? null, ...screen });
  }, [step, tick, view.wide, view.target]);

  useEffect(() => {
    if (step) primary.current?.focus({ preventScroll: true });
  }, [step]);

  const end = () => {
    if (run?.first) {
      qc.setQueryData<Me | null>(meKey, (m) => (m ? { ...m, tour_seen: true } : m));
      seen.mutate();
    }
    setRun(null);
  };
  const move = (by: number) => {
    if (!run) return;
    const at = run.at + by;
    if (at < 0) return;
    if (at >= run.steps.length) end();
    else setRun({ ...run, at });
  };

  // Esc ends it; the arrows move as the page reads (right to left: ← is the next one)
  useEffect(() => {
    if (!open) return;
    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") end();
      else if (e.key === "ArrowLeft") move(1);
      else if (e.key === "ArrowRight") move(-1);
      else return;
      e.preventDefault();
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  });

  if (!run || !step) return null;
  const count = run.steps.length;
  const last = run.at === count - 1;
  const { hole, at } = view;
  const arrow = at && at.side !== "free"
    ? at.side === "below" || at.side === "above" ? { left: at.arrow - 7 } : { top: at.arrow - 7 }
    : null;

  return createPortal(
    <div className="fixed inset-0 z-[60]">
      <svg aria-hidden className="absolute inset-0" width={view.width} height={view.height}>
        <path className="tour-dim" fillRule="evenodd" d={dimPath(view, hole)} />
      </svg>
      {hole ? <span aria-hidden className="tour-ring absolute rounded-[16px]" style={{ left: hole.left, top: hole.top, width: hole.width, height: hole.height }} /> : null}
      <div ref={bubble} role="dialog" aria-modal="true" aria-labelledby={`${id}-title`} aria-describedby={`${id}-body`}
        className="tour-card absolute w-[min(360px,calc(100vw-32px))] rounded-[22px] px-4 pt-3.5 pb-4 text-ink"
        style={{ left: at?.left ?? 0, top: at?.top ?? 0, visibility: at ? "visible" : "hidden" }}>
        <p className="sr-only" aria-live="polite">{`الخطوة ${run.at + 1} من ${count}: ${step.title}`}</p>
        {/* each step's words come in with the menus' short fade (none when the person asked for less motion) */}
        <div key={step.id} className="menu-pop">
          <div className="flex min-h-9 items-center gap-2">
            <span aria-hidden className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 text-[12px] leading-6 font-bold text-gold-ink">
              {run.at + 1} من {count}
            </span>
            {last ? null : (
              <button type="button" onClick={end}
                className="-my-1.5 -me-2 ms-auto min-h-11 rounded-full px-3 text-[13px] font-semibold text-ink-soft transition hover:bg-gold-tint hover:text-gold-ink">
                تخطّي
              </button>
            )}
          </div>
          <div className="mt-2 flex items-center gap-3">
            <StepIcon icon={step.icon} />
            <h2 id={`${id}-title`} className="text-[17px] leading-snug font-bold">{step.title}</h2>
          </div>
          <p id={`${id}-body`} className="mt-2 text-[14px] leading-7 text-ink-soft"><Words text={step.body(view)} /></p>
          <div aria-hidden className="mt-3 h-1 overflow-hidden rounded-full bg-line">
            <span className="block h-full rounded-full bg-gradient-to-l from-[#c99c68] via-gold to-gold-deep" style={{ width: `${((run.at + 1) / count) * 100}%` }} />
          </div>
          <div className="mt-3.5 flex items-center justify-end gap-2">
            {run.at > 0 ? (
              <button type="button" onClick={() => move(-1)}
                className="min-h-11 rounded-full border border-line bg-card px-4 text-[14px] font-semibold text-ink transition hover:border-gold-line hover:text-gold-ink">
                السابق
              </button>
            ) : null}
            <button ref={primary} type="button" onClick={() => move(1)}
              className="btn-gold min-h-11 rounded-full border px-5 text-[14px] font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_8px_18px_-8px_rgb(140_98_58/0.8)] transition hover:brightness-105">
              {step.next ?? "التالي"}
            </button>
          </div>
        </div>
        {arrow && at ? <span aria-hidden className="tour-arrow" data-side={at.side} style={arrow} /> : null}
      </div>
    </div>,
    document.body,
  );
}

/**
 * A bubble's words; «Ctrl K» as the two keys of the search box (left to right, so the «و» before it stays apart), kept on
 * one line with the word it is part of.
 */
function Words({ text }: { text: string }) {
  const [before = "", after] = text.split("Ctrl K");
  if (after === undefined) return <>{text}</>;
  const cut = before.lastIndexOf(" ") + 1;
  const key = "rounded-md border border-line border-b-2 bg-card px-1.5 font-sans text-[11px] leading-5 font-semibold text-ink";
  return (
    <>
      {before.slice(0, cut)}
      <span className="whitespace-nowrap">
        {before.slice(cut)}
        <span dir="ltr" className="ms-0.5 inline-flex gap-1 align-middle"><kbd className={key}>Ctrl</kbd><kbd className={key}>K</kbd></span>
      </span>
      {after}
    </>
  );
}

function StepIcon({ icon }: { icon: TourStep["icon"] }) {
  if (icon === "seal") return <OwnerImg name="logo_emblem" className="size-12 shrink-0" />;
  return (
    <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-[12px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
      <Icon name={icon} className="size-5" />
    </span>
  );
}
