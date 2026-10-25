import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, label, PAYMENT_STATES, URF_ANSWERS, URF_KINDS, URF_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Summary } from "../../shared/ui/caseCard";
import { confirmAction, toast } from "../../shared/ui/feedback";
import type { Folder } from "../../shared/ui/folders";
import { Icon } from "../../shared/ui/Icon";
import { Button, Chip, Empty, ErrorState, Loading, PageHeader, Tabs } from "../../shared/ui/ui";
import { listQuery, fileUrl, urf, type Show, type UrfCase } from "./api";

const TONE: Record<string, string> = {
  collecting: "grey", documents_pending: "blue", booked: "orange", approved: "green", lawyer_rejected: "grey",
  rejected_age: "grey", rejected_guardian: "grey", redirected_consultation: "grey",
};
const EDGE: Record<string, Folder | "grey"> = {
  collecting: "grey", documents_pending: "cobalt", booked: "saffron", approved: "jade", lawyer_rejected: "grey",
  rejected_age: "grey", rejected_guardian: "grey", redirected_consultation: "grey",
};
const SHOWS = ["open", "done"] as const;
const yesNo = (v: boolean | null) => (v == null ? "—" : v ? "أيوه" : "لأ");

/** What the page shows first, fetched while the pointer is still on its link. */
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
  const open = useQuery(listQuery("open"));
  const count = (...states: string[]) => open.data?.filter((c) => states.includes(c.state)).length;
  const tabs: [Show, string][] = [["open", "شغالة"], ["done", "اتقفلت"]];

  return (
    <div className="grid gap-5">
      <PageHeader title="حالات العرفي" description="عقد الجواز والطلاق العرفي: إجابات العميل، والورق، والميعاد في المكتب. الأستاذ يراجع الورق قبل الميعاد." />
      <Summary items={[
        { label: "بيجاوبوا على الأسئلة", value: count("collecting"), folder: "cobalt" },
        { label: "بيختاروا ميعاد", value: count("documents_pending"), folder: "saffron" },
        { label: "محجوزة ومحتاجة مراجعة", value: count("booked"), folder: "orchid" },
      ]} />
      <Tabs label="حالات العرفي" value={show} options={tabs} onChange={setShow} />
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? (
          <div className="surface rounded-[var(--radius-card)]">
            <Empty title={show === "open" ? "مفيش حالات شغالة" : "مفيش حالات اتقفلت لسه"} icon="scale">
              {show === "open" ? "لما عميل يطلب عقد أو طلاق عرفي، إجاباته وورقه هيظهروا هنا." : null}
            </Empty>
          </div>
        ) : (
          <ul className="grid gap-3">{list.data.map((c) => <CaseCardUrf key={c.id} c={c} />)}</ul>
        )
      )}
    </div>
  );
}

function CaseCardUrf({ c }: { c: UrfCase }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const review = useMutation({
    mutationFn: (d: "approved" | "rejected") => urf.review(c.id, d),
    onSuccess: (_, d) => {
      void qc.invalidateQueries({ queryKey: ["urf"] }); void qc.invalidateQueries({ queryKey: ["payments"] });
      toast(d === "approved" ? "اتوافق، وحازم هيبعت للعميل التأكيد" : "اترفضت، والميعاد اتلغى والفلوس هتترد");
    },
  });
  const answers: [string, string][] = c.kind === "marriage"
    ? [
        ["السن", c.husband_age != null ? `الزوج ${c.husband_age} - الزوجة ${c.wife_age}` : "—"],
        ["الزوجة", label(URF_ANSWERS, c.wife_past ?? "") || "—"],
        ["الولي", label(URF_ANSWERS, c.guardian ?? "") || "—"],
        ["الشهود", label(URF_ANSWERS, c.witnesses ?? "") || "—"],
      ]
    : [
        ["نسخة العقد", yesNo(c.has_copy)], ["متفقين", yesNo(c.agreed)], ["أطفال", yesNo(c.children)],
        ["الشهود", label(URF_ANSWERS, c.witnesses ?? "") || "—"],
      ];
  const mayReview = can(me, "urf.review") && c.state === "booked";

  return (
    <CaseCard edge={EDGE[c.state] ?? "grey"} label={c.name ?? "من غير اسم"}>
      <CaseHead
        title={c.name ?? "من غير اسم"} kind={label(URF_KINDS, c.kind)} kindFolder="orchid"
        meta={[
          <Meta key="w" icon="clock">بدأ {cairoWhen(c.created_at)}</Meta>,
          c.start ? <Meta key="a" icon="calendar">الميعاد {cairoWhen(c.start)} - {label(APPOINTMENT_STATES, c.appointment ?? "")}</Meta> : null,
          c.payment ? <Meta key="m" icon="wallet">{label(PAYMENT_STATES, c.payment)}</Meta> : null,
          <Meta key="p" icon="whatsapp"><span dir="ltr">+{c.wa_id}</span></Meta>,
        ]}
        status={<Chip tone={TONE[c.state] ?? "grey"}>{label(URF_STATES, c.state)}</Chip>} />
      <Facts items={answers} />
      <p className="px-5 pt-3 text-[13px] text-ink-soft"><span className="font-medium text-ink">المطلوب:</span> {c.documents}</p>
      <Files label="ورق العميل" count={c.files} href={(i) => fileUrl(c.id, i)} />
      {c.files === 0 && c.state === "booked" ? <CaseNote>لسه مفيش ورق. حازم طلبه من العميل بعد صورة التحويل.</CaseNote> : null}
      {c.decided_by ? <CaseNote>القرار: {c.decided_by} - {cairoWhen(c.decided_at!)}</CaseNote> : null}
      {c.notice === "failed" ? <CaseNote tone="bad">رسالة القرار ما وصلتش للعميل (غالبًا عدّى 24 ساعة من آخر رسالة منه). كلّموه.</CaseNote> : null}

      <ActionBar>
        {mayReview ? (
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="primary" busy={review.isPending && review.variables === "approved"}
              onClick={async () => { if (await confirmAction({ title: "موافق على الورق؟", yes: "موافق", body: "حازم هيبعت للعميل تأكيد الميعاد ومين لازم يحضر." })) review.mutate("approved"); }}>
              <Icon name="check-circle" className="size-4" />موافق
            </Button>
            <Button variant="danger" busy={review.isPending && review.variables === "rejected"}
              onClick={async () => { if (await confirmAction({ title: "ترفض الحالة؟", yes: "ارفض", danger: true, body: "الميعاد هيتلغي من المكتب، واللي اتدفع هيترد كامل، وحازم هيبلّغ العميل." })) review.mutate("rejected"); }}>
              <Icon name="x" className="size-4" />رفض
            </Button>
            {review.isError ? <span className="text-[13.5px] text-bad" role="alert">{errorMessage(review.error)}</span> : null}
          </div>
        ) : null}
      </ActionBar>
    </CaseCard>
  );
}
