import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { label, PAYMENT_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Chip } from "../../shared/ui/ui";
import { pounds, receiptUrl, type Payment } from "./api";

/**
 * The buttons that open a payment's receipt images (the owner: the bare picture in a new browser tab, on a black page, looked
 * unfinished). One button per image; it opens the viewer below inside the panel.
 */
export function ReceiptButtons({ p, tone }: { p: Payment; tone: string }) {
  const [open, setOpen] = useState<number | null>(null);
  if (p.receipts === 0) return null;
  return (
    <>
      <ul className="flex flex-wrap gap-2 px-5 pt-3" aria-label="صور التحويل">
        {Array.from({ length: p.receipts }, (_, i) => (
          <li key={i}>
            <button type="button" onClick={() => setOpen(i)}
              className="inline-flex min-h-10 items-center gap-1.5 rounded-full border border-gold-line bg-gold-tint/70 px-4 text-sm font-semibold text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)] transition hover:bg-gold-tint">
              <Icon name="receipt" className="size-4" />فتح الإيصال{p.receipts > 1 ? ` ${i + 1}` : ""}
            </button>
          </li>
        ))}
      </ul>
      {open !== null ? <ReceiptViewer p={p} tone={tone} index={open} onIndex={setOpen} onClose={() => setOpen(null)} /> : null}
    </>
  );
}

/**
 * A receipt image in a glass window over the page (Esc or a click outside closes it): the picture framed on the left, and on the
 * right what the person needs to check it against: who, what was asked, what arrived and when. The picture is the same private
 * address as before (never cached, every opening recorded), so opening it here is recorded like opening it in a tab.
 */
function ReceiptViewer({ p, tone, index, onIndex, onClose }: { p: Payment; tone: string; index: number; onIndex: (i: number) => void; onClose: () => void }) {
  const [zoom, setZoom] = useState(false);
  const [failed, setFailed] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const src = receiptUrl(p.appointment_id, index);

  useEffect(() => {
    closeRef.current?.focus();
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", key);
    const scroll = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", key); document.body.style.overflow = scroll; };
  }, [onClose]);
  // another image of the same payment: it starts again at the fit size, not loaded yet
  useEffect(() => { setZoom(false); setFailed(false); setLoaded(false); }, [index]);

  const rows: [string, React.ReactNode][] = [
    ["العميل", p.name ?? "من غير اسم"],
    ["المطلوب", <><span className="tabular">{pounds(p.amount_due)}</span> جنيه</>],
    ["وصل", p.amount_received != null ? <><span className="tabular">{pounds(p.amount_received)}</span> جنيه</> : "لسه ما اتسجّلش"],
    ["الإيصال وصل", p.proof_at ? cairoWhen(p.proof_at) : "—"],
  ];

  return createPortal(
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/45 p-3 backdrop-blur-md sm:p-6" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label="الإيصال"
        className="liquid liquid-strong grid max-h-[94dvh] w-full max-w-4xl overflow-hidden rounded-[28px] md:grid-cols-[minmax(0,1.3fr)_minmax(15rem,0.7fr)]">
        {/* the picture, framed on a soft warm stage */}
        <div className="relative grid min-h-[16rem] place-items-center bg-gradient-to-br from-gold-tint/70 via-card-2/60 to-card/40 p-4 sm:p-6">
          <div className="soft-scroll relative grid max-h-[68dvh] min-h-[12rem] w-full place-items-center overflow-auto rounded-[22px] border border-white/70 bg-white/70 p-2 shadow-[inset_0_1px_0_rgb(255_255_255/0.9),0_24px_48px_-24px_rgb(60_44_20/0.55)]">
            {!loaded && !failed ? <span aria-hidden className="absolute inset-0 animate-pulse rounded-[22px] bg-card-2/60" /> : null}
            {failed ? (
              <p className="flex items-center gap-2 px-4 py-10 text-sm text-red-ink" role="alert">
                <Icon name="alert-circle" className="size-5" />الصورة ما فتحتش. جرّب «افتح في تبويب جديد».
              </p>
            ) : (
              <img key={src} src={src} alt="صورة التحويل" onLoad={() => setLoaded(true)} onError={() => setFailed(true)}
                onClick={() => setZoom((z) => !z)}
                className={`rounded-[16px] ${zoom ? "max-w-none w-[170%] cursor-zoom-out" : "max-h-[64dvh] w-auto max-w-full cursor-zoom-in object-contain"}`} />
            )}
          </div>
          {p.receipts > 1 ? (
            <div className="mt-3 flex items-center gap-2 text-[13px]">
              <button type="button" disabled={index <= 0} onClick={() => onIndex(index - 1)} aria-label="الصورة اللي قبلها"
                className="grid size-9 place-items-center rounded-full border border-line/60 bg-card/70 disabled:opacity-40"><Icon name="chevron-end" className="size-4" /></button>
              <span className="tabular rounded-full bg-card/70 px-3 py-1 font-semibold">{index + 1} من {p.receipts}</span>
              <button type="button" disabled={index >= p.receipts - 1} onClick={() => onIndex(index + 1)} aria-label="الصورة اللي بعدها"
                className="grid size-9 place-items-center rounded-full border border-line/60 bg-card/70 disabled:opacity-40"><Icon name="chevron-start" className="size-4" /></button>
            </div>
          ) : null}
        </div>

        {/* what to check it against */}
        <div className="soft-scroll grid content-start gap-4 overflow-y-auto p-5 sm:p-6">
          <div className="flex items-start justify-between gap-3">
            <div className="grid gap-1">
              <h2 className="flex items-center gap-2 text-[18px] font-bold">
                <span aria-hidden className="grid size-9 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
                  <Icon name="receipt" className="size-5" />
                </span>
                إيصال التحويل
              </h2>
              <Chip tone={tone}>{label(PAYMENT_STATES, p.state)}</Chip>
            </div>
            <button ref={closeRef} type="button" onClick={onClose} aria-label="اقفل"
              className="grid size-9 shrink-0 place-items-center rounded-full border border-line/60 bg-card/70 transition hover:bg-card-2">
              <Icon name="x" className="size-4" />
            </button>
          </div>

          <dl className="grid gap-2">
            {rows.map(([term, value]) => (
              <div key={term} className="grid gap-0.5 rounded-2xl border border-line/60 bg-card/55 px-3.5 py-2.5 shadow-[inset_0_1px_0_rgb(255_255_255/0.4)]">
                <dt className="text-[11.5px] text-ink-soft">{term}</dt>
                <dd className="text-[14px] font-semibold">{value}</dd>
              </div>
            ))}
          </dl>

          <p className="flex items-start gap-1.5 text-[12px] text-ink-soft">
            <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
            قارن اللي في الصورة (المبلغ والتاريخ ومرجع التحويل) بحساب InstaPay أو المحفظة، وبعدها سجّل اللي وصل فعلًا من الكارت تحت. لمس الصورة بيكبّرها.
          </p>

          <div className="flex flex-wrap gap-2">
            <a href={src} target="_blank" rel="noopener noreferrer"
              className="inline-flex min-h-10 items-center gap-1.5 rounded-full border border-line/70 bg-card/70 px-4 text-sm font-semibold transition hover:border-gold-line hover:bg-gold-tint">
              <Icon name="forward" className="size-4" />افتح في تبويب جديد
            </a>
            <button type="button" onClick={() => setZoom((z) => !z)}
              className="inline-flex min-h-10 items-center gap-1.5 rounded-full border border-line/70 bg-card/70 px-4 text-sm font-semibold transition hover:border-gold-line hover:bg-gold-tint">
              <Icon name="search" className="size-4" />{zoom ? "صغّر" : "كبّر"}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
