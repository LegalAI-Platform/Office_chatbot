import { useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, PAYMENT_STATES, REFUND_REASONS, SERVICE_KINDS, SERVICE_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Phone } from "../../shared/ui/caseCard";
import { toast } from "../../shared/ui/feedback";
import { Button, Card, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { payments, pounds, receiptUrl, type Payment, type Review, type Show } from "./api";

const TONE: Record<string, string> = {
  requested: "grey", proof_received: "blue", duplicate_suspected: "orange", verified: "green", partial: "orange",
  not_received: "red", refund_requested: "orange", refunded: "grey",
};

const SHOWS: Show[] = ["review", "refunds", "all"];
const listQuery = (show: Show) => queryOptions({ queryKey: ["payments", show], queryFn: () => payments.list(show) });

/** The tab the page opens on and the refunds, fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(listQuery("review"));
  void qc.prefetchQuery(listQuery("refunds"));
}

/**
 * «الإيصالات والمدفوعات» (TASK 09, DECISIONS v1.13): the system never confirms a payment. Whoever checks
 * the office's InstaPay or wallet opens the receipt and marks what really came (P1: the lawyer, the manager or
 * the staff); a refund due is transferred by a person, who records it here (P2). Hazem tells the client.
 */
export function PaymentsPage() {
  const [show, setShow] = useUrlState<Show>("show", "review", SHOWS);
  const list = useQuery(listQuery(show));
  const tabs: [Show, string][] = [["review", "مستني مراجعتك"], ["refunds", "استرداد مطلوب"], ["all", "الكل"]];

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">الإيصالات والمدفوعات</h1>
      <p className="text-sm text-ink-soft">
        حازم بيأكد الميعاد بصورة التحويل بس. افتح الإيصال، اتأكد إن الفلوس وصلت، وعلّم اللي لقيته.
      </p>
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="المدفوعات">
        {tabs.map(([t, text]) => (
          <Button key={t} role="tab" aria-selected={t === show} variant={t === show ? "primary" : "secondary"} onClick={() => setShow(t)}>{text}</Button>
        ))}
      </div>
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? (
          <Card><Empty title="مفيش حاجة هنا" icon="check-circle" /></Card>
        ) : (
          <ul className="grid gap-3">{list.data.map((p) => <PaymentCard key={p.appointment_id} p={p} />)}</ul>
        )
      )}
    </div>
  );
}

function PaymentCard({ p }: { p: Payment }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const [amount, setAmount] = useState(String(p.amount_received ?? p.amount_due));
  const [ask, setAsk] = useState<Review | "refunded" | null>(null);
  const [method, setMethod] = useState("");
  const [note, setNote] = useState("");
  const done = () => { setAsk(null); void qc.invalidateQueries({ queryKey: ["payments"] }); void qc.invalidateQueries({ queryKey: ["booking"] }); };
  const review = useMutation({
    mutationFn: (s: Review) => payments.review(p.appointment_id, s, s === "not_received" ? undefined : Number(amount)),
    onSuccess: (_, s) => { done(); toast(s === "verified" ? "اتسجّل إن التحويل وصل ✅" : "اتسجّل، وحازم هيبلّغ العميل"); },
  });
  const refunded = useMutation({ mutationFn: () => payments.refunded(p.appointment_id, method, note || undefined), onSuccess: () => { done(); toast("الاسترداد اتسجّل، وحازم هيبلّغ العميل ✅"); } });
  const reviewable = can(me, "payments.verify") && ["proof_received", "duplicate_suspected", "verified", "partial", "not_received"].includes(p.state);
  const received = Number(amount);
  const what: Record<Review, string> = {
    verified: received > p.amount_due ? `تمام، ووصل ${pounds(received)} جنيه: الزيادة (${pounds(received - p.amount_due)}) هتتعمل بيها مهمة استرداد.` : `تمام: وصل ${pounds(received)} جنيه.`,
    partial: `ناقص: وصل ${pounds(received)} من ${pounds(p.amount_due)}. حازم هيبعت للعميل يكمّل ${pounds(p.amount_due - received)} جنيه.`,
    not_received: "ما وصلش: حازم هيطلب من العميل يتأكد ويبعت الصورة تاني.",
  };

  const service = p.order ? label(SERVICE_KINDS, p.service) : label(BOOKING_SERVICES, p.service);
  const facts: [string, ReactNode][] = [
    ["المطلوب", <><span className="tabular">{pounds(p.amount_due)}</span> جنيه</>],
    ["وصل", p.amount_received != null ? <><span className="tabular">{pounds(p.amount_received)}</span> جنيه</> : "—"],
    ["الإيصال وصل", p.proof_at ? cairoWhen(p.proof_at) : "—"],
    [p.order ? "الطلب" : "الميعاد", p.order ? label(SERVICE_STATES, p.appointment) : label(APPOINTMENT_STATES, p.appointment)],
  ];

  return (
    <CaseCard tone={TONE[p.state] ?? "grey"} label={`${p.name ?? "من غير اسم"}: ${service}`}>
      <CaseHead title={p.name ?? "من غير اسم"} kind={service}
        meta={[
          <Meta key="w" icon="clock">{cairoWhen(p.start)}</Meta>,
          <Phone key="p" wa={p.wa_id} />,
          p.reviewed_by ? <span key="r">راجعه {p.reviewed_by}{p.reviewed_at ? `، ${cairoWhen(p.reviewed_at)}` : ""}</span> : null,
        ]}
        status={<>
          <Chip tone={TONE[p.state] ?? "grey"}>{label(PAYMENT_STATES, p.state)}</Chip>
          {p.duplicate ? <Chip tone="orange">نفس الصورة على حجز تاني</Chip> : null}
        </>} />
      <Facts items={facts} />
      <Files label="صور التحويل" count={p.receipts} href={(i) => receiptUrl(p.appointment_id, i)} word="فتح الإيصال" icon="receipt" />
      {p.state === "refunded" ? (
        <CaseNote>اترد {pounds(p.refund_due ?? 0)} جنيه على {p.refund_method} · {p.refunded_by}{p.refunded_at ? `، ${cairoWhen(p.refunded_at)}` : ""}</CaseNote>
      ) : null}

      <ActionBar>
        {reviewable ? (
          <div className="grid gap-2">
            <div className="flex flex-wrap items-end gap-2">
              <Field label="المبلغ اللي وصل فعلًا (جنيه)" type="number" min={1} inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <Button variant="primary" disabled={!(received >= p.amount_due)} onClick={() => setAsk("verified")}>تمام</Button>
              <Button disabled={!(received > 0 && received < p.amount_due)} onClick={() => setAsk("partial")}>ناقص</Button>
              <Button variant="danger" onClick={() => setAsk("not_received")}>ما وصلش</Button>
            </div>
            {ask && ask !== "refunded" ? (
              <Sure text={what[ask]} busy={review.isPending} onYes={() => review.mutate(ask)} onNo={() => setAsk(null)} />
            ) : null}
            {review.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(review.error)}</p> : null}
          </div>
        ) : null}

        {p.state === "refund_requested" && can(me, "payments.refund") ? (
          <div className="grid gap-2">
            <p className="text-sm font-semibold">
              استرداد {pounds(p.refund_due ?? 0)} جنيه{p.refund_reason ? ` (${label(REFUND_REASONS, p.refund_reason)})` : ""}.
              {!p.reviewed_by ? " لسه ما اتراجعش." : ""}
            </p>
            <div className="grid gap-2 md:grid-cols-2">
              <Field label="حوّلته على إيه" hint="مثلًا فودافون كاش أو InstaPay" maxLength={100} value={method} onChange={(e) => setMethod(e.target.value)} />
              <Field label="ملاحظة (اختياري)" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
            </div>
            <div><Button variant="primary" disabled={method.trim() === ""} onClick={() => setAsk("refunded")}>اتحوّل</Button></div>
            {ask === "refunded" ? (
              <Sure text={`إنت حوّلت ${pounds(p.refund_due ?? 0)} جنيه فعلًا على ${method}؟ حازم هيبلّغ العميل إن المبلغ اترد.`} busy={refunded.isPending}
                onYes={() => refunded.mutate()} onNo={() => setAsk(null)} />
            ) : null}
            {refunded.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(refunded.error)}</p> : null}
          </div>
        ) : null}
      </ActionBar>
    </CaseCard>
  );
}

function Sure({ text, busy, onYes, onNo }: { text: string; busy: boolean; onYes: () => void; onNo: () => void }) {
  return (
    <div className="grid gap-2 rounded-lg border border-gold-line bg-gold-tint p-3 text-sm" role="alertdialog" aria-label="تأكيد">
      <p>{text}</p>
      <div className="flex gap-2">
        <Button variant="primary" busy={busy} onClick={onYes}>أيوه، أكّد</Button>
        <Button onClick={onNo}>لأ</Button>
      </div>
    </div>
  );
}
