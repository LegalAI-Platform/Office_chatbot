import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, PAYMENT_STATES, REFUND_REASONS } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { payments, pounds, receiptUrl, type Payment, type Review, type Show } from "./api";

const TONE: Record<string, string> = {
  requested: "grey", proof_received: "blue", duplicate_suspected: "orange", verified: "green", partial: "orange",
  not_received: "red", refund_requested: "orange", refunded: "grey",
};

/**
 * «الإيصالات والمدفوعات» (TASK 09, DECISIONS v1.13): the system never confirms a payment. Whoever checks
 * the office's InstaPay or wallet opens the receipt and marks what really came (P1: the lawyer, the manager or
 * the staff); a refund due is transferred by a person, who records it here (P2). Hazem tells the client.
 */
export function PaymentsPage() {
  const [show, setShow] = useState<Show>("review");
  const list = useQuery({ queryKey: ["payments", show], queryFn: () => payments.list(show) });
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
  const review = useMutation({ mutationFn: (s: Review) => payments.review(p.appointment_id, s, s === "not_received" ? undefined : Number(amount)), onSuccess: done });
  const refunded = useMutation({ mutationFn: () => payments.refunded(p.appointment_id, method, note || undefined), onSuccess: done });
  const reviewable = can(me, "payments.verify") && ["proof_received", "duplicate_suspected", "verified", "partial", "not_received"].includes(p.state);
  const received = Number(amount);
  const what: Record<Review, string> = {
    verified: received > p.amount_due ? `تمام، ووصل ${pounds(received)} جنيه: الزيادة (${pounds(received - p.amount_due)}) هتتعمل بيها مهمة استرداد.` : `تمام: وصل ${pounds(received)} جنيه.`,
    partial: `ناقص: وصل ${pounds(received)} من ${pounds(p.amount_due)}. حازم هيبعت للعميل يكمّل ${pounds(p.amount_due - received)} جنيه.`,
    not_received: "ما وصلش: حازم هيطلب من العميل يتأكد ويبعت الصورة تاني.",
  };

  return (
    <li>
      <Card>
        <div className="flex flex-wrap items-start gap-3">
          <span className="grid min-w-0 flex-1 gap-0.5">
            <span className="font-semibold">{p.name ?? "من غير اسم"} · {cairoWhen(p.start)}</span>
            <span className="text-xs text-ink-soft">
              {label(BOOKING_SERVICES, p.service)} · {label(APPOINTMENT_STATES, p.appointment)} · <span dir="ltr">+{p.wa_id}</span>
            </span>
            <span className="text-sm">
              المطلوب <b className="tabular">{pounds(p.amount_due)}</b> جنيه
              {p.amount_received != null ? <> · وصل <b className="tabular">{pounds(p.amount_received)}</b></> : null}
              {p.proof_at ? <> · الإيصال وصل {cairoWhen(p.proof_at)}</> : null}
            </span>
            {p.reviewed_by ? <span className="text-xs text-ink-soft">راجعه {p.reviewed_by}{p.reviewed_at ? `، ${cairoWhen(p.reviewed_at)}` : ""}</span> : null}
          </span>
          <span className="flex flex-wrap gap-1.5">
            <Chip tone={TONE[p.state] ?? "grey"}>{label(PAYMENT_STATES, p.state)}</Chip>
            {p.duplicate ? <Chip tone="orange">نفس الصورة على حجز تاني</Chip> : null}
          </span>
        </div>

        {p.receipts > 0 ? (
          <ul className="flex flex-wrap gap-2" aria-label="صور التحويل">
            {Array.from({ length: p.receipts }, (_, i) => (
              <li key={i}>
                <a href={receiptUrl(p.appointment_id, i)} target="_blank" rel="noopener noreferrer"
                  className="inline-flex min-h-10 items-center gap-1.5 rounded-[var(--radius-control)] border border-line px-3 text-sm hover:border-gold-line">
                  <Icon name="receipt" className="size-4" />فتح الإيصال{p.receipts > 1 ? ` ${i + 1}` : ""}
                </a>
              </li>
            ))}
          </ul>
        ) : null}

        {reviewable ? (
          <div className="grid gap-2 border-t border-line pt-3">
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
          <div className="grid gap-2 border-t border-line pt-3">
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

        {p.state === "refunded" ? (
          <p className="text-sm text-ink-soft">اترد {pounds(p.refund_due ?? 0)} جنيه على {p.refund_method} · {p.refunded_by}{p.refunded_at ? `، ${cairoWhen(p.refunded_at)}` : ""}</p>
        ) : null}
      </Card>
    </li>
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
