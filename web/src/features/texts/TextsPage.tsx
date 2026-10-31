import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import type { ApprovedText, TextVersion } from "../../shared/api/types";
import { cairoWhen } from "../../shared/lib/time";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Button, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { toast } from "../../shared/ui/feedback";

const ERRORS: Record<string, string> = {
  too_long: "النص أطول من الحد المسموح للرسالة دي.",
  placeholder: "النص فيه {{...}}: اكتب الكلام نفسه، مش مكان فاضي.",
};

// how a text is changed
const STEPS: { icon: IconName; title: string; text: string }[] = [
  { icon: "search", title: "دوّر على الرسالة", text: "بكلمة منها، أو باسمها بالإنجليزي." },
  { icon: "document", title: "اضغط «عدّل» واكتب", text: "الكلام نفسه، من غير {{...}}." },
  { icon: "check-circle", title: "«احفظ نسخة جديدة»", text: "بتشتغل من الرسالة الجاية على طول." },
];

/**
 * Hazem's approved texts (TASK 07c, the manager and the lawyer): what he says, word for word. A
 * change is a new version, active at once; every older version stays and comes back with a click.
 * As «liquid glass»: a header saying what these texts are and the three steps of a change, a glass search, and each text a
 * glass card with the words in a message bubble, as the client reads them.
 */
export function TextsPage() {
  const texts = useQuery({ queryKey: ["texts"], queryFn: () => api<ApprovedText[]>("GET", "/texts") });
  const [q, setQ] = useState("");
  const shown = texts.data?.filter((t) => !q.trim() || t.text.includes(q.trim()) || t.key.includes(q.trim())) ?? [];

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
        <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
        <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
        <div className="relative grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(0,1.35fr)] xl:items-center">
          <div className="flex items-start gap-4">
            <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
              <Icon name="message" className="size-7" />
            </span>
            <div className="grid min-w-0 gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="text-[26px] leading-tight font-bold">نصوص حازم</h1>
                {texts.data ? (
                  <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{texts.data.length} نص</span>
                ) : null}
              </div>
              <p className="text-[14px] leading-relaxed text-ink-soft">
                ده الكلام اللي حازم بيبعته للعملاء <strong className="text-ink">بالحرف</strong>. أي تعديل بيشتغل على طول من الرسالة الجاية، والنسخة القديمة بتفضل محفوظة وترجع بضغطة.
              </p>
            </div>
          </div>
          <ol className="grid gap-2 sm:grid-cols-3" aria-label="إزاي تعدّل نص">
            {STEPS.map((s, i) => (
              <li key={s.title} className="relative flex items-start gap-3 rounded-[16px] border border-line/60 bg-card/55 p-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
                <span className="tabular grid size-7 shrink-0 place-items-center rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] text-[13px] font-bold text-[#2a2117] shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                  {i + 1}
                </span>
                <span className="grid gap-0.5">
                  <span className="text-[13.5px] font-bold">{s.title}</span>
                  <span className="text-[12px] leading-snug text-ink-soft">{s.text}</span>
                </span>
              </li>
            ))}
          </ol>
        </div>
      </header>

      <div className="liquid flex min-h-12 max-w-xl items-center gap-2 rounded-full px-4">
        <Icon name="search" className="size-4 shrink-0 text-gold-ink" />
        <input className="min-h-11 min-w-0 flex-1 bg-transparent text-ink outline-none placeholder:text-ink-soft" placeholder="دوّر في النصوص..." aria-label="دوّر في النصوص"
          value={q} onChange={(e) => setQ(e.target.value)} />
        {q ? (
          <button type="button" onClick={() => setQ("")} className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[13px] text-ink-soft hover:bg-card-2 hover:text-ink">
            <Icon name="x" className="size-4" /> امسح البحث
          </button>
        ) : null}
      </div>

      {texts.isPending ? <Loading /> : texts.isError ? <ErrorState message={errorMessage(texts.error)} onRetry={() => void texts.refetch()} /> : shown.length === 0 ? (
        <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
          <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
            <Icon name="search" className="size-7" />
          </span>
          <p className="text-[16px] font-bold">{q.trim() ? "مفيش نص فيه الكلام ده" : "مفيش نصوص لسه"}</p>
          {q.trim() ? <p className="max-w-sm text-[13px] text-ink-soft">جرّب كلمة تانية، أو امسح البحث وشوف الكل.</p> : null}
        </div>
      ) : (
        <ul className="grid gap-3">
          {shown.map((t) => <li key={t.key}><TextCard text={t} /></li>)}
        </ul>
      )}
    </div>
  );
}

function TextCard({ text }: { text: ApprovedText }) {
  const qc = useQueryClient();
  const [draft, setDraft] = useState<string | null>(null);
  const [history, setHistory] = useState(false);
  const save = useMutation({
    mutationFn: () => api<{ version: number }>("POST", `/texts/${text.key}`, { text: draft }),
    onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", text.key] }); toast("النص اتحفظ ✅"); },
  });
  const length = draft?.length ?? 0;
  const error = save.error instanceof ApiError && save.error.code ? ERRORS[save.error.code] ?? errorMessage(save.error) : undefined;
  const over = length > text.max_chars;

  return (
    <article className={`relative grid gap-3 overflow-hidden rounded-[18px] border bg-card/60 py-4 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)] backdrop-blur-md transition motion-reduce:transition-none ${draft === null ? "border-line/60 hover:border-gold-line" : "border-gold-line"}`}>
      <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${draft === null ? "bg-gold/60" : "bg-gold"}`} />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <code className="rounded-md border border-line/60 bg-card/70 px-2 py-0.5 text-xs text-ink-soft" dir="ltr">{text.key}</code>
        <span className="flex flex-wrap items-center gap-2 text-xs text-ink-soft">
          <Chip tone="gold">نسخة {text.version}</Chip>
          <span className="inline-flex items-center gap-1">
            <Icon name="clock" className="size-3.5" />
            {text.updated_by ? `عدّلها ${text.updated_by} · ` : ""}{cairoWhen(text.updated_at)}
          </span>
        </span>
      </div>
      {draft === null ? (
        <>
          {/* the words as the client reads them: a message bubble */}
          <div className="grid gap-1">
            <span className="flex items-center gap-1 text-[11.5px] text-ink-soft"><Icon name="whatsapp" className="size-3.5" />حازم بيقول</span>
            <p className="font-display max-w-3xl rounded-[18px] rounded-ss-[6px] border border-line/60 bg-gold-tint/50 px-4 py-3 text-lg leading-relaxed whitespace-pre-wrap shadow-[inset_0_1px_0_rgb(255_255_255/0.4)]">{text.text}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => setDraft(text.text)}>عدّل</Button>
            {text.versions > 1 ? <Button variant="ghost" onClick={() => setHistory((h) => !h)}>{history ? "اخفي النسخ القديمة" : `النسخ القديمة (${text.versions - 1})`}</Button> : null}
          </div>
        </>
      ) : (
        <div className="grid gap-2">
          <textarea className="soft-scroll min-h-28 rounded-[16px] border border-line/60 bg-card/70 p-3 text-base leading-relaxed text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold" value={draft}
            onChange={(e) => setDraft(e.target.value)} aria-label={`نص ${text.key}`} />
          <div className="flex flex-wrap items-center gap-3">
            <span aria-hidden className="block h-1.5 w-32 overflow-hidden rounded-full bg-line/80">
              <span className={`block h-1.5 rounded-full ${over ? "bg-red" : "bg-gradient-to-l from-[#dcb98c] to-[#a87a48]"}`} style={{ width: `${Math.min(100, (length / text.max_chars) * 100)}%` }} />
            </span>
            <span className={`tabular text-xs ${over ? "text-bad" : "text-ink-soft"}`}>{length} من {text.max_chars} حرف</span>
          </div>
          {error ? <p className="text-sm text-bad" role="alert">{error}</p> : save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="primary" busy={save.isPending} disabled={!draft.trim() || over || draft.trim() === text.text}
              onClick={() => save.mutate()}>احفظ نسخة جديدة</Button>
            <Button variant="ghost" onClick={() => { setDraft(null); save.reset(); }}>إلغاء</Button>
            <span className="text-[12px] text-ink-soft">النسخة دي هتفضل محفوظة، وترجّعها وقت ما تحب.</span>
          </div>
        </div>
      )}
      {history ? <History textKey={text.key} /> : null}
    </article>
  );
}

function History({ textKey }: { textKey: string }) {
  const qc = useQueryClient();
  const versions = useQuery({ queryKey: ["text", textKey], queryFn: () => api<TextVersion[]>("GET", `/texts/${textKey}`) });
  const activate = useMutation({
    mutationFn: (version: number) => api<void>("POST", `/texts/${textKey}/versions/${version}/activate`),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", textKey] }); toast("النسخة دي بقت الشغالة ✅"); },
  });
  if (versions.isPending) return <Loading />;
  if (versions.isError) return <ErrorState message={errorMessage(versions.error)} />;
  return (
    <div className="grid gap-2 border-t border-line/60 pt-3">
      <p className="flex items-center gap-1.5 text-[12.5px] text-ink-soft">
        <Icon name="info" className="size-3.5 shrink-0 text-gold-ink" />
        النسخ اللي كانت شغالة قبل كده. «رجّع النسخة دي» تخليها هي اللي حازم يقولها من دلوقتي.
      </p>
      <ol className="soft-scroll grid max-h-[28rem] gap-2 overflow-y-auto">
        {versions.data.filter((v) => !v.active).map((v) => (
          <li key={v.version} className="grid gap-1.5 rounded-[14px] border border-line/60 bg-card/45 p-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.35)]">
            <span className="flex flex-wrap items-center gap-2 text-xs text-ink-soft">
              نسخة {v.version} · {v.source}{v.updated_by ? ` · ${v.updated_by}` : ""} · {cairoWhen(v.updated_at)}
            </span>
            <p className="whitespace-pre-wrap text-sm">{v.text}</p>
            <div><Button variant="ghost" busy={activate.isPending && activate.variables === v.version} onClick={() => activate.mutate(v.version)}>رجّع النسخة دي</Button></div>
          </li>
        ))}
      </ol>
    </div>
  );
}
