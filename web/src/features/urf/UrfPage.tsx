import type { ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, label, PAYMENT_STATES, URF_ANSWERS, URF_KINDS, URF_STATES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Phone } from "../../shared/ui/caseCard";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { fileUrl, urf, type Show, type UrfCase } from "./api";

const TONE: Record<string, string> = {
  collecting: "grey", documents_pending: "blue", booked: "orange", approved: "green", lawyer_rejected: "grey",
  rejected_age: "grey", rejected_guardian: "grey", redirected_consultation: "grey",
};
const yesNo = (v: boolean | null) => (v == null ? "—" : v ? "أيوه" : "لأ");

const SHOWS: Show[] = ["open", "done"];
const listQuery = (show: Show) => queryOptions({ queryKey: ["urf", show], queryFn: () => urf.list(show), refetchInterval: 60_000 });

/** The open cases (the tab the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(listQuery("open"));
}

const TABS: { value: Show; label: string; icon: IconName }[] = [
  { value: "open", label: "شغالة", icon: "play" },
  { value: "done", label: "اتقفلت", icon: "check-circle" },
];

/**
 * «حالات العرفي» (TASK 11, DECISIONS v1.15 B-11 / S-06): Hazem's questions and the client's answers, the documents
 * (ID cards, opened here only), the appointment in the office and its payment. The lawyer or the manager approves the
 * case before the appointment (the client gets the confirmation with who must come) or refuses it (the appointment is
 * cancelled by the office and what was paid goes back).
 */
export function UrfPage() {
  const [show, setShow] = useUrlState<Show>("show", "open", SHOWS);
  const list = useQuery(listQuery(show));

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro count={list.data?.length} />

      <section aria-label="الفلاتر" className="liquid grid gap-2 rounded-[22px] p-4 sm:p-5">
        <div className="flex gap-1 overflow-x-auto rounded-full border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="tablist" aria-label="حالات العرفي">
          {TABS.map((t) => {
            const on = t.value === show;
            return (
              <button key={t.value} type="button" role="tab" aria-selected={on} onClick={() => setShow(t.value)}
                className={`flex min-h-9 shrink-0 grow items-center justify-center gap-1.5 whitespace-nowrap rounded-full px-3.5 text-[13.5px] transition ${on
                  ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                  : "text-ink hover:bg-card/80"}`}>
                <Icon name={t.icon} className="size-4" />
                {t.label}
              </button>
            );
          })}
        </div>
      </section>

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? <Nothing /> : (
          <ul className={GLASS_LIST}>{list.data.map((c) => <UrfCard key={c.id} c={c} />)}</ul>
        )
      )}
    </div>
  );
}

/** The shared case cards as glass: see-through over the page's light, a lit edge, a lift under the pointer. */
const GLASS_LIST = "grid gap-3 [&>li>article]:rounded-[20px] [&>li>article]:border-line/60 [&>li>article]:bg-card/60 [&>li>article]:shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_8px_24px_-14px_rgb(60_44_20/0.4)] [&>li>article]:backdrop-blur-md [&>li>article]:transition [&>li>article:hover]:-translate-y-px [&>li>article:hover]:bg-card/80 motion-reduce:[&>li>article]:transition-none motion-reduce:[&>li>article:hover]:translate-y-0 [&_dl>div]:border [&_dl>div]:border-line/50 [&_dl>div]:bg-card/55";

function Intro({ count }: { count?: number | undefined }) {
  return (
    <header className="relative">
      <div className="relative">
        <div className="flex items-start gap-4">
          <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="shield" className="size-6" />
          </span>
          <div className="grid min-w-0 gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-[26px] leading-tight font-bold">حالات العرفي</h1>
              {count !== undefined ? (
                <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{count} حالة</span>
              ) : null}
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}

function Nothing() {
  return (
    <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
      <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-green-bg to-card text-green-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="check-circle" className="size-7" />
      </span>
      <p className="text-[16px] font-bold">مفيش حالات هنا</p>
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
      {c.state === "booked" && !mayReview ? <CaseNote>مستنية المستشار أو المدير يراجع الورق.</CaseNote> : null}
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
