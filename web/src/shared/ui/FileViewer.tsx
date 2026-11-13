import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Icon } from "./Icon";

/**
 * v1.81 (the owner: the client's papers opened as a bare picture in a new tab, on a black page; «اعملها زي … الإيصال»): one of
 * the client's files in the same glass window as the receipt, over the page (Esc or a click outside closes it). A picture is
 * framed and zooms on a touch; a PDF shows inside the window; anything the browser cannot show says so, with «افتح في تبويب
 * جديد». The address is the same private one as before (never cached, every opening recorded).
 */
export function FileViewer({ title, count, href, index, onIndex, onClose, details }: {
  title: string; count: number; href: (i: number) => string; index: number; onIndex: (i: number) => void; onClose: () => void;
  details?: [string, ReactNode][];
}) {
  const [zoom, setZoom] = useState(false);
  const [notImage, setNotImage] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const src = href(index);

  useEffect(() => {
    closeRef.current?.focus();
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", key);
    const scroll = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", key); document.body.style.overflow = scroll; };
  }, [onClose]);
  // another file: it starts again at the fit size, not loaded yet
  useEffect(() => { setZoom(false); setNotImage(false); setLoaded(false); }, [index]);

  const button = "inline-flex min-h-10 items-center gap-1.5 rounded-full border border-line/70 bg-card/70 px-4 text-sm font-semibold transition hover:border-gold-line hover:bg-gold-tint";
  return createPortal(
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/45 p-3 backdrop-blur-md sm:p-6" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label={title}
        className="liquid liquid-strong grid max-h-[94dvh] w-full max-w-4xl overflow-hidden rounded-[28px] md:grid-cols-[minmax(0,1.3fr)_minmax(15rem,0.7fr)]">
        <div className="relative grid min-h-[16rem] place-items-center bg-gradient-to-br from-gold-tint/70 via-card-2/60 to-card/40 p-4 sm:p-6">
          <div className="soft-scroll relative grid max-h-[68dvh] min-h-[12rem] w-full place-items-center overflow-auto rounded-[22px] border border-white/70 bg-white/70 p-2 shadow-[inset_0_1px_0_rgb(255_255_255/0.9),0_24px_48px_-24px_rgb(60_44_20/0.55)]">
            {!loaded && !notImage ? <span aria-hidden className="absolute inset-0 animate-pulse rounded-[22px] bg-card-2/60" /> : null}
            {notImage ? (
              // not a picture (a PDF, mostly): shown by the browser inside the window, or the way to open it
              <object data={src} type="application/pdf" aria-label={`${title} ${index + 1}`} className="h-[64dvh] w-full rounded-[16px]">
                <p className="flex items-center gap-2 px-4 py-10 text-sm text-ink-soft" role="status">
                  <Icon name="info" className="size-5" />هذا الملف ليس صورة. افتحه من «افتح في تبويب جديد».
                </p>
              </object>
            ) : (
              <img key={src} src={src} alt={`${title} ${index + 1}`} onLoad={() => setLoaded(true)} onError={() => setNotImage(true)}
                onClick={() => setZoom((z) => !z)}
                className={`rounded-[16px] ${zoom ? "max-w-none w-[170%] cursor-zoom-out" : "max-h-[64dvh] w-auto max-w-full cursor-zoom-in object-contain"}`} />
            )}
          </div>
          {count > 1 ? (
            <div className="mt-3 flex items-center gap-2 text-[13px]">
              <button type="button" disabled={index <= 0} onClick={() => onIndex(index - 1)} aria-label="الملف السابق"
                className="grid size-9 place-items-center rounded-full border border-line/60 bg-card/70 disabled:opacity-40"><Icon name="chevron-end" className="size-4" /></button>
              <span className="tabular rounded-full bg-card/70 px-3 py-1 font-semibold">{index + 1} من {count}</span>
              <button type="button" disabled={index >= count - 1} onClick={() => onIndex(index + 1)} aria-label="الملف التالي"
                className="grid size-9 place-items-center rounded-full border border-line/60 bg-card/70 disabled:opacity-40"><Icon name="chevron-start" className="size-4" /></button>
            </div>
          ) : null}
        </div>

        <div className="soft-scroll grid content-start gap-4 overflow-y-auto p-5 sm:p-6">
          <div className="flex items-start justify-between gap-3">
            <h2 className="flex items-center gap-2 text-[18px] font-bold">
              <span aria-hidden className="grid size-9 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
                <Icon name="document" className="size-5" />
              </span>
              {title}
            </h2>
            <button ref={closeRef} type="button" onClick={onClose} aria-label="إغلاق"
              className="grid size-9 shrink-0 place-items-center rounded-full border border-line/60 bg-card/70 transition hover:bg-card-2">
              <Icon name="x" className="size-4" />
            </button>
          </div>

          {details?.length ? (
            <dl className="grid gap-2">
              {details.map(([term, value]) => (
                <div key={term} className="grid gap-0.5 rounded-2xl border border-line/60 bg-card/55 px-3.5 py-2.5 shadow-[inset_0_1px_0_rgb(255_255_255/0.4)]">
                  <dt className="text-[11.5px] text-ink-soft">{term}</dt>
                  <dd className="text-[14px] font-semibold">{value}</dd>
                </div>
              ))}
            </dl>
          ) : null}

          <p className="flex items-start gap-1.5 text-[12px] text-ink-soft">
            <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
            المس الصورة لتكبيرها. يُسجَّل كل فتح للملف.
          </p>

          <div className="flex flex-wrap gap-2">
            <a href={src} target="_blank" rel="noopener noreferrer" className={button}>
              <Icon name="forward" className="size-4" />افتح في تبويب جديد
            </a>
            {!notImage ? (
              <button type="button" onClick={() => setZoom((z) => !z)} className={button}>
                <Icon name="search" className="size-4" />{zoom ? "صغّر" : "كبّر"}
              </button>
            ) : null}
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
