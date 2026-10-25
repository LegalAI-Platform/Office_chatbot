import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import type { ApprovedText, TextVersion } from "../../shared/api/types";
import { cairoWhen } from "../../shared/lib/time";
import { toast } from "../../shared/ui/feedback";
import { Icon } from "../../shared/ui/Icon";
import { Button, Chip, Empty, ErrorState, Loading, PageHeader, Segmented } from "../../shared/ui/ui";
import { useUrlState } from "../../shared/lib/url";

const ERRORS: Record<string, string> = {
  too_long: "النص أطول من الحد المسموح للرسالة دي.",
  placeholder: "النص فيه {{...}}: اكتب الكلام نفسه، مش مكان فاضي.",
};

/**
 * Hazem's approved texts (TASK 07c, the manager and the lawyer): what he says, word for word. A
 * change is a new version, active at once; every older version stays and comes back with a click.
 */
export function TextsPage() {
  const texts = useQuery({ queryKey: ["texts"], queryFn: () => api<ApprovedText[]>("GET", "/texts") });
  const [q, setQ] = useState("");
  const [topic, setTopic] = useUrlState<Topic>("topic", "all", TOPIC_KEYS);
  const [edited, setEdited] = useState(false);
  const term = q.trim();
  const all = texts.data ?? [];
  const shown = all
    .filter((t) => !term || t.text.includes(term) || t.key.includes(term))
    .filter((t) => !edited || t.versions > 1)
    .filter((t) => topic === "all" || topicOf(t.key) === topic);
  const count = (k: Topic) => all.filter((t) => k === "all" || topicOf(t.key) === k).length;

  return (
    <div className="grid gap-5">
      <PageHeader title="نصوص حازم" description="ده الكلام اللي حازم بيبعته للعملاء بالحرف. أي تعديل بيشتغل على طول من الرسالة الجاية، والنسخة القديمة بتفضل محفوظة وترجع بضغطة." />
      <div className="grid gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <label className="relative block w-full sm:w-80">
            <Icon name="search" className="pointer-events-none absolute inset-y-0 start-3 my-auto size-[18px] text-ink-faint" />
            <input className="surface min-h-10 w-full rounded-[var(--radius-control)] ps-10 pe-3" placeholder="دوّر في كلام حازم" aria-label="دوّر في النصوص"
              value={q} onChange={(e) => setQ(e.target.value)} />
          </label>
          <label className="flex items-center gap-2 rounded-[var(--radius-control)] border border-line bg-card px-3 py-2 text-[14px]">
            <input type="checkbox" checked={edited} onChange={(e) => setEdited(e.target.checked)} />اللي اتعدّل بس
          </label>
          <span className="tabular ms-auto text-[13.5px] text-ink-soft">{shown.length} من {all.length} نص</span>
        </div>
        <Segmented label="الموضوع" value={topic} onChange={setTopic}
          options={TOPICS.map(([k, t]) => [k, `${t} ${count(k)}`] as const)} />
      </div>
      {texts.isPending ? <Loading /> : texts.isError ? <ErrorState message={errorMessage(texts.error)} onRetry={() => void texts.refetch()} /> : shown.length === 0 ? (
        <div className="surface rounded-[var(--radius-card)]"><Empty title="مفيش نصوص بالشكل ده" icon="chat">جرّب كلمة تانية أو موضوع تاني.</Empty></div>
      ) : (
        TOPICS.filter(([k]) => k !== "all" && (topic === "all" || topic === k)).map(([k, title]) => {
          const list = shown.filter((t) => topicOf(t.key) === k);
          return list.length ? (
            <section key={k} aria-label={title} className="surface overflow-hidden rounded-[var(--radius-card)]">
              <h2 className="flex items-baseline gap-2 border-b border-line bg-card-2/60 px-5 py-2.5 text-[15px] font-semibold">
                {title}<span className="tabular text-[13px] font-normal text-ink-soft">{list.length}</span>
              </h2>
              <ul className="grid divide-y divide-line">{list.map((t) => <li key={t.key}><TextCard text={t} /></li>)}</ul>
            </section>
          ) : null;
        })
      )}
    </div>
  );
}

/** Hazem's texts by the part of the conversation they belong to (from their keys). */
type Topic = "all" | "start" | "booking" | "money" | "services" | "urf" | "prices" | "buttons" | "other";
const TOPICS: readonly (readonly [Topic, string])[] = [
  ["all", "الكل"], ["start", "البداية"], ["booking", "الحجز"], ["money", "الدفع والاسترداد"], ["services", "الخدمات المكتوبة"],
  ["urf", "العرفي"], ["prices", "الأسعار"], ["buttons", "الزراير"], ["other", "حالات خاصة"],
];
const TOPIC_KEYS = TOPICS.map(([k]) => k);
const START = new Set(["welcome_ask_name", "greeting", "ask_name_again", "ask_topic", "topic_received", "existing_or_new", "existing_matter", "thanks", "human_request"]);
function topicOf(key: string): Topic {
  if (key.startsWith("btn_")) return "buttons";
  if (key.startsWith("urf_")) return "urf";
  if (key.startsWith("price_") || key === "urgent_price") return "prices";
  if (key.startsWith("booking_") || key === "reschedule_or_cancel" || key === "consultation_offer") return "booking";
  if (key.startsWith("payment_") || key.startsWith("refund_") || key === "receipt_send_image" || key === "proof_received_again") return "money";
  if (key.startsWith("service_") || key.endsWith("_request") && key !== "human_request" || key === "written_opinion") return "services";
  if (START.has(key)) return "start";
  return "other";
}

/** Who changed a text, in words a person uses: a migration is nobody's name. */
const byWhom = (by?: string | null): string =>
  !by || by.startsWith("migration") ? "" : by.startsWith("owner approval") ? "اتوافق عليها - " : `عدّلها ${by} - `;

function TextCard({ text }: { text: ApprovedText }) {
  const qc = useQueryClient();
  const [draft, setDraft] = useState<string | null>(null);
  const [history, setHistory] = useState(false);
  const save = useMutation({
    mutationFn: () => api<{ version: number }>("POST", `/texts/${text.key}`, { text: draft }),
    onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", text.key] }); toast("النص اتحفظ، وحازم هيستخدمه من الرسالة الجاية"); },
  });
  const length = draft?.length ?? 0;
  const error = save.error instanceof ApiError && save.error.code ? ERRORS[save.error.code] ?? errorMessage(save.error) : undefined;

  return (
    <div className="grid gap-3 p-4 lg:px-5">
      <div className="flex flex-wrap items-center justify-between gap-2 text-[13px] text-ink-soft">
        <span className="flex items-center gap-2">
          <Chip tone="accent">نسخة {text.version}</Chip>
          <span>{byWhom(text.updated_by)}{cairoWhen(text.updated_at)}</span>
        </span>
        <code className="text-[12px] text-ink-faint" dir="ltr">{text.key}</code>
      </div>
      {draft === null ? (
        <>
          <p className="max-w-[70ch] text-[15.5px] leading-[1.85] whitespace-pre-wrap">{text.text}</p>
          <div className="flex flex-wrap gap-2">
            <Button size="sm" onClick={() => setDraft(text.text)}>عدّل</Button>
            {text.versions > 1 ? <Button size="sm" variant="ghost" onClick={() => setHistory((h) => !h)}>{history ? "اخفي النسخ القديمة" : `النسخ القديمة (${text.versions - 1})`}</Button> : null}
          </div>
        </>
      ) : (
        <div className="grid gap-2">
          <textarea className="surface min-h-28 rounded-[var(--radius-control)] p-3 text-base leading-relaxed" value={draft}
            onChange={(e) => setDraft(e.target.value)} aria-label={`نص ${text.key}`} />
          <span className={`tabular text-xs ${length > text.max_chars ? "text-bad" : "text-ink-soft"}`}>{length} من {text.max_chars} حرف</span>
          {error ? <p className="text-sm text-bad" role="alert">{error}</p> : save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
          <div className="flex flex-wrap gap-2">
            <Button variant="primary" busy={save.isPending} disabled={!draft.trim() || length > text.max_chars || draft.trim() === text.text}
              onClick={() => save.mutate()}>احفظ نسخة جديدة</Button>
            <Button variant="ghost" onClick={() => { setDraft(null); save.reset(); }}>إلغاء</Button>
          </div>
        </div>
      )}
      {history ? <History textKey={text.key} /> : null}
    </div>
  );
}

function History({ textKey }: { textKey: string }) {
  const qc = useQueryClient();
  const versions = useQuery({ queryKey: ["text", textKey], queryFn: () => api<TextVersion[]>("GET", `/texts/${textKey}`) });
  const activate = useMutation({
    mutationFn: (version: number) => api<void>("POST", `/texts/${textKey}/versions/${version}/activate`),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", textKey] }); toast("النسخة القديمة رجعت"); },
  });
  if (versions.isPending) return <Loading />;
  if (versions.isError) return <ErrorState message={errorMessage(versions.error)} />;
  return (
    <ol className="grid gap-2 border-t border-line pt-3">
      {versions.data.filter((v) => !v.active).map((v) => (
        <li key={v.version} className="grid gap-1 rounded-[var(--radius-control)] bg-muted p-3">
          <span className="flex flex-wrap items-center gap-2 text-xs text-ink-soft">
            نسخة {v.version} - {byWhom(v.updated_by)}{cairoWhen(v.updated_at)}
          </span>
          <p className="whitespace-pre-wrap text-sm">{v.text}</p>
          <div><Button variant="ghost" busy={activate.isPending && activate.variables === v.version} onClick={() => activate.mutate(v.version)}>رجّع النسخة دي</Button></div>
        </li>
      ))}
    </ol>
  );
}
