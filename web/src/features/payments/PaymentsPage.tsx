import { useState } from "react";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, PAYMENT_STATES, REFUND_REASONS, SERVICE_KINDS, SERVICE_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Summary } from "../../shared/ui/caseCard";
import { confirmAction, toast } from "../../shared/ui/feedback";
import type { Folder } from "../../shared/ui/folders";
import { Button, Chip, Empty, ErrorState, Field, Loading, PageHeader, Tabs } from "../../shared/ui/ui";
import { listQuery, payments, pounds, receiptUrl, type Payment, type Review, type Show } from "./api";

const TONE: Record<string, string> = {
  requested: "grey", proof_received: "orange", duplicate_suspected: "orange", verified: "green", partial: "orange",
  not_received: "red", refund_requested: "orange", refunded: "grey",
};
const EDGE: Record<string, Folder | "grey"> = {
  requested: "grey", proof_received: "saffron", duplicate_suspected: "saffron", verified: "jade", partial: "saffron",
  not_received: "coral", refund_requested: "saffron", refunded: "grey",
};
const SHOWS = ["review", "refunds", "all"] as const;

/** What the page shows first, fetched while the pointer is still on its link. */
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
  const review = useQuery(listQuery("review"));
  const refunds = useQuery(listQuery("refunds"));
  const tabs: [Show, string][] = [["review", "مستني مراجعتك"], ["refunds", "استرداد مطلوب"], ["all", "الكل"]];

  return (
    <div className="grid gap-5">
      <PageHeader title="الإيصالات والمدفوعات" description="حازم بيأكد الميعاد بصورة التحويل بس. افتح الإيصال، اتأكد إن الفلوس وصلت، وعلّم اللي لقيته." />
      <Summary items={[
        { label: "مستني مراجعة", value: review.data?.length, folder: "saffron" },
        { label: "جنيه مستنية تتأكد", value: review.data?.reduce((s, p) => s + p.amount_due, 0), folder: "ink" },
        { label: "استرداد مطلوب", value: refunds.data?.length, folder: "orchid" },
      ]} />
      <Tabs label="المدفوعات" value={show} options={tabs} onChange={setShow} />
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? (
          <div className="surface rounded-[var(--radius-card)]">
            <Empty title={show === "review" ? "مفيش إيصالات مستنية مراجعة" : show === "refunds" ? "مفيش استرداد مطلوب" : "مفيش مدفوعات لسه"} icon="wallet">
              {show === "review" ? "أول ما عميل يبعت صورة تحويل هتظهر هنا تراجعها." : null}
            </Empty>
          </div>
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
  const [method, setMethod] = useState("");
  const [note, setNote] = useState("");
  const refresh = () => { void qc.invalidateQueries({ queryKey: ["payments"] }); void qc.invalidateQueries({ queryKey: ["booking"] }); void qc.invalidateQueries({ queryKey: ["appointments"] }); };
  const review = useMutation({
    mutationFn: (s: Review) => payments.review(p.appointment_id, s, s === "not_received" ? undefined : Number(amount)),
    onSuccess: (_, s) => { refresh(); toast(s === "verified" ? "اتعلّم إن الفلوس وصلت" : s === "partial" ? "اتعلّم ناقص، وحازم هيبلّغ العميل" : "اتعلّم ما وصلش، وحازم هيبلّغ العميل"); },
  });
  const refunded = useMutation({
    mutationFn: () => payments.refunded(p.appointment_id, method, note || undefined),
    onSuccess: () => { refresh(); toast("الاسترداد اتسجّل، وحازم هيبلّغ العميل"); },
  });
  const reviewable = can(me, "payments.verify") && ["proof_received", "duplicate_suspected", "verified", "partial", "not_received"].includes(p.state);
  const received = Number(amount);
  const ask: Record<Review, { title: string; body: string; yes: string; danger?: boolean }> = {
    verified: {
      title: "الفلوس وصلت كاملة؟", yes: "أيوه، وصلت",
      body: received > p.amount_due ? `وصل ${pounds(received)} جنيه: الزيادة (${pounds(received - p.amount_due)}) هتتعمل بيها مهمة استرداد.` : `وصل ${pounds(received)} جنيه، والحجز هيتأكد.`,
    },
    partial: { title: "المبلغ ناقص؟", yes: "أيوه، ناقص", body: `وصل ${pounds(received)} من ${pounds(p.amount_due)}. حازم هيبعت للعميل يكمّل ${pounds(p.amount_due - received)} جنيه.` },
    not_received: { title: "الفلوس ما وصلتش؟", yes: "أيوه، ما وصلتش", danger: true, body: "حازم هيطلب من العميل يتأكد من التحويل ويبعت الصورة تاني." },
  };
  const mark = async (s: Review) => { if (await confirmAction(ask[s])) review.mutate(s); };

  return (
    <CaseCard edge={EDGE[p.state] ?? "grey"} label={p.name ?? "من غير اسم"}>
      <CaseHead
        title={p.name ?? "من غير اسم"}
        kind={p.order ? label(SERVICE_KINDS, p.service) : label(BOOKING_SERVICES, p.service)} kindFolder={p.order ? "saffron" : "cobalt"}
        meta={[
          <Meta key="w" icon="calendar">{p.order ? "اتطلب" : "الميعاد"} {cairoWhen(p.start)}</Meta>,
          <Meta key="s" icon="info">{p.order ? label(SERVICE_STATES, p.appointment) : label(APPOINTMENT_STATES, p.appointment)}</Meta>,
          <Meta key="p" icon="whatsapp"><span dir="ltr">+{p.wa_id}</span></Meta>,
        ]}
        status={<>
          <Chip tone={TONE[p.state] ?? "grey"}>{label(PAYMENT_STATES, p.state)}</Chip>
          {p.duplicate ? <Chip tone="orange">نفس الصورة على حجز تاني</Chip> : null}
        </>} />
      <Facts items={[
        ["المطلوب", <><span className="display">{pounds(p.amount_due)}</span> <span className="text-[12px] font-normal text-ink-soft">جنيه</span></>],
        ["وصل", p.amount_received != null ? <><span className="display">{pounds(p.amount_received)}</span> <span className="text-[12px] font-normal text-ink-soft">جنيه</span></> : "—",
          p.amount_received != null ? (p.amount_received >= p.amount_due ? "text-jade-ink" : "text-saffron-ink") : undefined],
        ["الإيصال وصل", p.proof_at ? cairoWhen(p.proof_at) : "لسه"],
        ["راجعه", p.reviewed_by ? `${p.reviewed_by}${p.reviewed_at ? `، ${cairoWhen(p.reviewed_at)}` : ""}` : "لسه"],
      ]} />
      <Files label="صور التحويل" count={p.receipts} href={(i) => receiptUrl(p.appointment_id, i)} word="فتح الإيصال" icon="receipt" />
      {p.state === "refunded" ? (
        <CaseNote>اترد {pounds(p.refund_due ?? 0)} جنيه على {p.refund_method}، سجّله {p.refunded_by}{p.refunded_at ? `، ${cairoWhen(p.refunded_at)}` : ""}.</CaseNote>
      ) : null}

      <ActionBar>
        {reviewable ? (
          <div className="flex flex-wrap items-end gap-2">
            <div className="w-44"><Field label="المبلغ اللي وصل فعلًا (جنيه)" type="number" min={1} inputMode="numeric" value={amount} onChange={(e) => setAmount(e.target.value)} /></div>
            <Button variant="primary" busy={review.isPending && review.variables === "verified"} disabled={!(received >= p.amount_due)} onClick={() => void mark("verified")}>تمام</Button>
            <Button busy={review.isPending && review.variables === "partial"} disabled={!(received > 0 && received < p.amount_due)} onClick={() => void mark("partial")}>ناقص</Button>
            <Button variant="danger" busy={review.isPending && review.variables === "not_received"} onClick={() => void mark("not_received")}>ما وصلش</Button>
            {review.isError ? <p className="basis-full text-[13.5px] text-bad" role="alert">{errorMessage(review.error)}</p> : null}
          </div>
        ) : null}
        {p.state === "refund_requested" && can(me, "payments.refund") ? (
          <div className="grid gap-2">
            <p className="text-[14px] font-semibold">
              استرداد {pounds(p.refund_due ?? 0)} جنيه{p.refund_reason ? ` (${label(REFUND_REASONS, p.refund_reason)})` : ""}.
              {!p.reviewed_by ? " لسه ما اتراجعش." : ""}
            </p>
            <div className="grid items-end gap-2 md:grid-cols-[1fr_1fr_auto]">
              <Field label="حوّلته على إيه" hint="مثلًا فودافون كاش أو InstaPay" maxLength={100} value={method} onChange={(e) => setMethod(e.target.value)} />
              <Field label="ملاحظة (اختياري)" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
              <Button variant="primary" busy={refunded.isPending} disabled={method.trim() === ""}
                onClick={async () => {
                  if (await confirmAction({ title: "المبلغ اتحوّل فعلًا؟", yes: "أيوه، اتحوّل", body: `إنت حوّلت ${pounds(p.refund_due ?? 0)} جنيه على ${method}؟ حازم هيبلّغ العميل إن المبلغ اترد.` })) refunded.mutate();
                }}>اتحوّل</Button>
            </div>
            {refunded.isError ? <p className="text-[13.5px] text-bad" role="alert">{errorMessage(refunded.error)}</p> : null}
          </div>
        ) : null}
      </ActionBar>
    </CaseCard>
  );
}
