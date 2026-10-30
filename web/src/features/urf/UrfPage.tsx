import type { ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, label, PAYMENT_STATES, URF_ANSWERS, URF_KINDS, URF_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Phone } from "../../shared/ui/caseCard";
import { Icon } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Card, Chip, Empty, ErrorState, Loading } from "../../shared/ui/ui";
import { fileUrl, urf, type Show, type UrfCase } from "./api";

const TONE: Record<string, string> = {
  collecting: "grey", documents_pending: "blue", booked: "orange", approved: "green", lawyer_rejected: "grey",
  rejected_age: "grey", rejected_guardian: "grey", redirected_consultation: "grey",
};
const yesNo = (v: boolean | null) => (v == null ? "—" : v ? "أيوه" : "لأ");

const SHOWS: Show[] = ["open", "done"];
const listQuery = (show: Show) => queryOptions({ queryKey: ["urf", show], queryFn: () => urf.list(show) });

/** The open cases (the tab the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(listQuery("open"));
}

/**
 * «حالات العرفي» (TASK 11, DECISIONS v1.15 B-11 / S-06): Hazem's questions and the client's answers, the documents
 * (ID cards, opened here only), the appointment in the office and its payment. The lawyer or the manager approves the
 * case before the appointment (the client gets the confirmation with who must come) or refuses it (the appointment is
 * cancelled by the office and what was paid goes back).
 */
export function UrfPage() {
  const [show, setShow] = useUrlState<Show>("show", "open", SHOWS);
  const list = useQuery(listQuery(show));
  const tabs: [Show, string][] = [["open", "شغالة"], ["done", "اتقفلت"]];

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">حالات العرفي</h1>
      <p className="text-sm text-ink-soft">عقد الجواز والطلاق العرفي: إجابات العميل، والورق، والميعاد في المكتب. المستشار يراجع الورق قبل الميعاد.</p>
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="حالات العرفي">
        {tabs.map(([t, text]) => (
          <Button key={t} role="tab" aria-selected={t === show} variant={t === show ? "primary" : "secondary"} onClick={() => setShow(t)}>{text}</Button>
        ))}
      </div>
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? <Card><Empty title="مفيش حالات هنا" icon="check-circle" /></Card> : (
          <ul className="grid gap-3">{list.data.map((c) => <UrfCard key={c.id} c={c} />)}</ul>
        )
      )}
    </div>
  );
}

function UrfCard({ c }: { c: UrfCase }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const review = useMutation({
    mutationFn: (d: "approved" | "rejected") => urf.review(c.id, d),
    onSuccess: (_, d) => {
      void qc.invalidateQueries({ queryKey: ["urf"] });
      void qc.invalidateQueries({ queryKey: ["payments"] });
      toast(d === "approved" ? "اتوافق على الحالة، وحازم هيبلّغ العميل ✅" : "الحالة اترفضت، وحازم هيبلّغ العميل");
    },
  });
  const answers: [string, string][] = c.kind === "marriage"
    ? [
        ["السن", c.husband_age != null ? `الزوج ${c.husband_age} · الزوجة ${c.wife_age}` : "—"],
        ["الزوجة", label(URF_ANSWERS, c.wife_past ?? "") || "—"],
        ["الولي", label(URF_ANSWERS, c.guardian ?? "") || "—"],
        ["الشهود", label(URF_ANSWERS, c.witnesses ?? "") || "—"],
      ]
    : [
        ["نسخة العقد", yesNo(c.has_copy)], ["متفقين", yesNo(c.agreed)], ["أطفال", yesNo(c.children)],
        ["الشهود", label(URF_ANSWERS, c.witnesses ?? "") || "—"],
      ];
  const mayReview = can(me, "urf.review");

  return (
    <CaseCard tone={TONE[c.state] ?? "grey"} label={`${c.name ?? "من غير اسم"}: ${label(URF_KINDS, c.kind)}`}>
      <CaseHead title={c.name ?? "من غير اسم"} kind={label(URF_KINDS, c.kind)}
        meta={[
          <Meta key="w" icon="clock">بدأ {cairoWhen(c.created_at)}</Meta>,
          <Phone key="p" wa={c.wa_id} />,
          c.decided_by ? <span key="d">القرار: {c.decided_by} · {cairoWhen(c.decided_at!)}</span> : null,
        ]}
        status={<Chip tone={TONE[c.state] ?? "grey"}>{label(URF_STATES, c.state)}</Chip>} />
      <Facts items={[
        ...(c.start ? [["الميعاد", <>{cairoWhen(c.start)} · {label(APPOINTMENT_STATES, c.appointment ?? "")}</>] as [string, ReactNode]] : []),
        ...(c.payment ? [["الدفع", label(PAYMENT_STATES, c.payment)] as [string, ReactNode]] : []),
        ...answers,
      ]} />
      <CaseNote>المطلوب: {c.documents}</CaseNote>
      <Files label="ورق العميل" count={c.files} href={(i) => fileUrl(c.id, i)} word="مستند" />
      {c.files === 0 && c.state === "booked" ? <CaseNote>لسه مفيش ورق. حازم طلبه من العميل بعد صورة التحويل.</CaseNote> : null}
      {c.notice === "failed" ? <CaseNote tone="bad">رسالة القرار ما وصلتش للعميل (غالبًا عدّى 24 ساعة من آخر رسالة منه). كلّموه.</CaseNote> : null}

      <ActionBar>
        {c.state === "booked" && mayReview ? (
          <div className="flex flex-wrap gap-2">
            <Button variant="primary" busy={review.isPending}
              onClick={async () => {
                if (await confirmAction({ title: "موافق على الورق؟", body: "حازم هيبعت للعميل تأكيد الميعاد ومين لازم يحضر.", yes: "موافق" })) review.mutate("approved");
              }}>
              <Icon name="check-circle" className="size-4" />موافق
            </Button>
            <Button variant="ghost" busy={review.isPending}
              onClick={async () => {
                if (await confirmAction({ title: "ترفض الحالة؟", body: "الميعاد هيتلغي من المكتب، واللي اتدفع هيترد كامل، وحازم هيبلّغ العميل.", yes: "ارفض", danger: true })) review.mutate("rejected");
              }}>
              <Icon name="x" className="size-4" />رفض
            </Button>
            {review.isError ? <span className="text-sm text-bad" role="alert">{errorMessage(review.error)}</span> : null}
          </div>
        ) : null}
      </ActionBar>
    </CaseCard>
  );
}
