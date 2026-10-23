import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import type { ApprovedText, TextVersion } from "../../shared/api/types";
import { cairoWhen } from "../../shared/lib/time";
import { Button, Card, Chip, ErrorState, Loading } from "../../shared/ui/ui";

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
  const shown = texts.data?.filter((t) => !q.trim() || t.text.includes(q.trim()) || t.key.includes(q.trim())) ?? [];

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">نصوص حازم</h1>
      <p className="max-w-2xl text-sm text-ink-soft">
        ده الكلام اللي حازم بيبعته للعملاء بالحرف. أي تعديل بيشتغل على طول من الرسالة الجاية، والنسخة القديمة بتفضل محفوظة وترجع بضغطة.
      </p>
      <input className="surface min-h-11 max-w-md rounded-[var(--radius-control)] px-3" placeholder="دوّر في النصوص..." aria-label="دوّر في النصوص"
        value={q} onChange={(e) => setQ(e.target.value)} />
      {texts.isPending ? <Loading /> : texts.isError ? <ErrorState message={errorMessage(texts.error)} onRetry={() => void texts.refetch()} /> : (
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
    onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", text.key] }); },
  });
  const length = draft?.length ?? 0;
  const error = save.error instanceof ApiError && save.error.code ? ERRORS[save.error.code] ?? errorMessage(save.error) : undefined;

  return (
    <Card>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <code className="text-xs text-ink-soft" dir="ltr">{text.key}</code>
        <span className="flex flex-wrap items-center gap-2 text-xs text-ink-soft">
          <Chip tone="gold">نسخة {text.version}</Chip>
          {text.updated_by ? `عدّلها ${text.updated_by} · ` : ""}{cairoWhen(text.updated_at)}
        </span>
      </div>
      {draft === null ? (
        <>
          <p className="font-display text-lg leading-relaxed whitespace-pre-wrap">{text.text}</p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => setDraft(text.text)}>عدّل</Button>
            {text.versions > 1 ? <Button variant="ghost" onClick={() => setHistory((h) => !h)}>{history ? "اخفي النسخ القديمة" : `النسخ القديمة (${text.versions - 1})`}</Button> : null}
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
    </Card>
  );
}

function History({ textKey }: { textKey: string }) {
  const qc = useQueryClient();
  const versions = useQuery({ queryKey: ["text", textKey], queryFn: () => api<TextVersion[]>("GET", `/texts/${textKey}`) });
  const activate = useMutation({
    mutationFn: (version: number) => api<void>("POST", `/texts/${textKey}/versions/${version}/activate`),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["texts"] }); void qc.invalidateQueries({ queryKey: ["text", textKey] }); },
  });
  if (versions.isPending) return <Loading />;
  if (versions.isError) return <ErrorState message={errorMessage(versions.error)} />;
  return (
    <ol className="grid gap-2 border-t border-line pt-3">
      {versions.data.filter((v) => !v.active).map((v) => (
        <li key={v.version} className="grid gap-1 rounded-[var(--radius-control)] bg-muted p-3">
          <span className="flex flex-wrap items-center gap-2 text-xs text-ink-soft">
            نسخة {v.version} · {v.source}{v.updated_by ? ` · ${v.updated_by}` : ""} · {cairoWhen(v.updated_at)}
          </span>
          <p className="whitespace-pre-wrap text-sm">{v.text}</p>
          <div><Button variant="ghost" busy={activate.isPending && activate.variables === v.version} onClick={() => activate.mutate(v.version)}>رجّع النسخة دي</Button></div>
        </li>
      ))}
    </ol>
  );
}
