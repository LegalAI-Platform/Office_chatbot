import { useEffect, useState } from "react";
import { Link } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { ErrorState, Loading } from "../../shared/ui/ui";
import { documentUrl, useDocuments, type DocumentClient, type DocumentFile, type KindFilter, type SourceFilter } from "./api";

const KINDS: { value: KindFilter; label: string; icon: IconName }[] = [
  { value: "all", label: "الكل", icon: "document" },
  { value: "image", label: "الصور", icon: "camera" },
  { value: "document", label: "المستندات", icon: "document" },
];

const SOURCES: { value: SourceFilter; label: string }[] = [
  { value: "all", label: "من كل مكان" },
  { value: "message", label: "رسائل واتساب" },
  { value: "receipt", label: "صور التحويل" },
  { value: "service", label: "مستندات الخدمات" },
  { value: "urf", label: "مستندات العرفي" },
];

/** Where a file came from, as its chip says it, and the page that has it. */
const FROM: Record<DocumentFile["source"], { label: string; page: string; to: string }> = {
  message: { label: "رسالة واتساب", page: "المهمة", to: "/tasks" },
  receipt: { label: "صورة تحويل", page: "الإيصالات والمدفوعات", to: "/receipts" },
  service: { label: "مستند خدمة", page: "طلبات الخدمات", to: "/services" },
  urf: { label: "مستند عرفي", page: "حالات العرفي", to: "/urf" },
};

/** The name the client's phone gave the file, else what it is. */
export const fileTitle = (f: DocumentFile): string => f.name?.trim() || (f.kind === "image" ? "صورة" : f.mime === "application/pdf" ? "ملف PDF" : "مستند");

/** The page a file came from: its task (a message's file, when one was opened), else the page of its kind. */
export const fileContext = (f: DocumentFile): { label: string; to: string } | null =>
  f.source === "message" ? (f.task_id ? { label: FROM.message.page, to: `/tasks/${f.task_id}` } : null) : { label: FROM[f.source].page, to: FROM[f.source].to };

/**
 * «المستندات» (v1.110, the owner: «…/documents شوف دي عاوز تعملها»): every file a client sent, in one place, by client, the
 * client with the newest file first: the pictures and documents of his WhatsApp messages, his transfers' receipts, his
 * services' and Urfi cases' documents. A file opens in a new tab through the panel only, and its opening is recorded; no
 * picture is shown in the list itself (ID cards). Who sees a client's files is who sees his tasks.
 */
export function DocumentsPage() {
  const [kind, setKind] = useUrlState<KindFilter>("kind", "all", KINDS.map((k) => k.value));
  const [source, setSource] = useUrlState<SourceFilter>("from", "all", SOURCES.map((s) => s.value));
  const [page, setPage] = useState(1);
  // the search stays out of the address (a number typed would stay in the history): a quarter of a second after the last key
  const [text, setText] = useState("");
  const [q, setQ] = useState("");
  useEffect(() => {
    const t = setTimeout(() => { setQ(text.trim()); setPage(1); }, 250);
    return () => clearTimeout(t);
  }, [text]);
  const list = useDocuments(q, kind, source, page);
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / list.data.page_size)) : 1;

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <header className="relative flex items-start gap-4">
        <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
          <Icon name="document" className="size-6" />
        </span>
        <div className="grid gap-1">
          <h1 className="text-[26px] leading-tight font-bold">المستندات</h1>
          <p className="text-[13.5px] text-ink-soft">
            كل ما أرسله العملاء: الصور والمستندات في رسائل واتساب، وصور التحويل، ومستندات الخدمات والعرفي، مرتبة حسب العميل.
            {list.data ? ` · ${list.data.total} عميل` : ""}
          </p>
        </div>
      </header>

      <section aria-label="البحث والتصفية" className="liquid grid gap-3 rounded-[22px] p-4 max-sm:p-2.5 sm:p-5">
        <label className="flex min-h-11 items-center gap-2.5 rounded-full border border-line/70 bg-card/60 px-4 focus-within:border-gold">
          <Icon name="search" className="size-[18px] shrink-0 text-ink-soft" />
          <input type="search" value={text} onChange={(e) => setText(e.target.value)} aria-label="ابحث في المستندات" autoComplete="off"
            placeholder="اسم العميل أو آخر أرقام هاتفه..." className="min-h-10 min-w-0 flex-1 bg-transparent text-[14px] outline-none" />
        </label>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex gap-1 rounded-full border border-line/60 bg-card/45 p-1" role="tablist" aria-label="نوع الملف">
            {KINDS.map((k) => (
              <button key={k.value} type="button" role="tab" aria-selected={k.value === kind} onClick={() => { setKind(k.value); setPage(1); }}
                className={`flex min-h-9 items-center gap-1.5 rounded-full px-3.5 text-[13.5px] transition ${k.value === kind ? "btn-gold font-bold" : "text-ink hover:bg-card/80"}`}>
                <Icon name={k.icon} className="size-4" />
                {k.label}
              </button>
            ))}
          </div>
          <select value={source} onChange={(e) => { setSource(e.target.value as SourceFilter); setPage(1); }} aria-label="مصدر الملف"
            className="min-h-10 rounded-full border border-line/70 bg-card/60 px-3.5 text-[13.5px]">
            {SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </div>
      </section>

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.clients.length === 0 ? (
          <p className="liquid rounded-[22px] px-6 py-10 text-center text-[15px] text-ink-soft">
            {q ? "لا توجد مستندات لهذا البحث." : "لا توجد مستندات بعد."}
          </p>
        ) : (
          <>
            <ul className="grid gap-3">
              {list.data.clients.map((c) => <ClientDocuments key={c.id} c={c} />)}
            </ul>
            {pages > 1 ? (
              <nav aria-label="الصفحات" className="flex items-center justify-center gap-3 text-sm">
                <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}
                  className="min-h-10 rounded-full border border-line/70 bg-card/60 px-4 disabled:opacity-40">السابق</button>
                <span className="text-ink-soft">صفحة {page} من {pages}</span>
                <button type="button" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}
                  className="min-h-10 rounded-full border border-line/70 bg-card/60 px-4 disabled:opacity-40">التالي</button>
              </nav>
            ) : null}
          </>
        )
      )}
    </div>
  );
}

function ClientDocuments({ c }: { c: DocumentClient }) {
  const now = new Date();
  return (
    <li>
      <article aria-label={c.name ?? "رقم بلا اسم"}
        className="grid gap-3 rounded-[20px] border border-line/60 bg-card/60 p-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_8px_24px_-14px_rgb(60_44_20/0.4)] backdrop-blur-md">
        <header className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <h2 className="text-[16px] font-bold">{c.name ?? "رقم بلا اسم"}</h2>
          {c.phone_last3 ? <span className="text-xs text-ink-soft">رقمه ينتهي بـ {c.phone_last3}</span> : null}
          <span className="ms-auto text-xs text-ink-soft">
            {c.count > c.files.length ? `أحدث ${c.files.length} من ${c.count} ملفًا` : `${c.count} ${c.count === 1 ? "ملف" : c.count === 2 ? "ملفان" : "ملفات"}`}
          </span>
        </header>
        <ul className="grid gap-1.5 sm:grid-cols-2">
          {c.files.map((f) => {
            const where = fileContext(f);
            return (
              <li key={`${f.source}${f.id}${f.index}`} className="flex items-center gap-2.5 rounded-xl border border-line/50 bg-card/55 px-3 py-2">
                <Icon name={f.kind === "image" ? "camera" : "document"} className="size-5 shrink-0 text-gold-ink" />
                <span className="grid min-w-0 flex-1 gap-0.5">
                  <a href={documentUrl(f)} target="_blank" rel="noopener noreferrer" className="truncate text-sm font-semibold text-gold-ink hover:underline">
                    {fileTitle(f)}
                  </a>
                  <span className="truncate text-xs text-ink-soft">
                    {FROM[f.source].label} · {cairoWhen(f.received_at, now)}
                    {where ? <> · <Link to={where.to} className="hover:underline">{where.label}</Link></> : null}
                  </span>
                </span>
              </li>
            );
          })}
        </ul>
      </article>
    </li>
  );
}
