import type { ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { cairoWhen } from "../../shared/lib/time";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Button, ErrorState, Loading } from "../../shared/ui/ui";

type TestNumber = { key: string; masked: string };
type Forgot = { masked: string; rows: number; calendar_deleted: number; calendar_by_hand: string[]; chatwoot: "ok" | "failed" | "off" };

const testing = {
  numbers: () => api<{ numbers: TestNumber[] }>("GET", "/testing/numbers"),
  forget: (key: string) => api<Forgot>("POST", "/testing/forget", { key }),
  urgentWindow: () => api<{ on: boolean }>("GET", "/testing/urgent-window"),
  setUrgentWindow: (on: boolean) => api<{ on: boolean }>("PUT", "/testing/urgent-window", { on }),
  understanding: () => api<{ on: boolean }>("GET", "/testing/understanding"),
  setUnderstanding: (on: boolean) => api<{ on: boolean }>("PUT", "/testing/understanding", { on }),
  infoWriter: () => api<{ on: boolean }>("GET", "/testing/info-writer"),
  setInfoWriter: (on: boolean) => api<{ on: boolean }>("PUT", "/testing/info-writer", { on }),
  caseSaid: () => api<{ on: boolean }>("GET", "/testing/urf-case-said"),
  setCaseSaid: (on: boolean) => api<{ on: boolean }>("PUT", "/testing/urf-case-said", { on }),
};

/**
 * v1.84 (TASK 20): the Urfi case read as one list: what the client says, asks («ينفع ابوها؟») or takes back («اخوها مش هيجي»),
 * answered by the case's own rules. Off is the Urfi case exactly as v1.81 (the owner: «مقفول… بتاعت محمد، مفتوح يبقى شغلنا»).
 */
function CaseSaid() {
  const qc = useQueryClient();
  const state = useQuery({ queryKey: ["testing", "urf-case-said"], queryFn: testing.caseSaid, retry: false });
  const set = useMutation({
    mutationFn: testing.setCaseSaid,
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ["testing", "urf-case-said"] });
      toast(r.on ? "تم تفعيل طريقة العرفي الجديدة ✅" : "عاد العرفي كما كان في الإصدار 1.81 ✅");
    },
  });
  if (state.isError && !(state.error instanceof ApiError && state.error.status === 404)) {
    return <ErrorState message={errorMessage(state.error)} onRetry={() => void state.refetch()} />;
  }
  if (!state.data) return null;
  const on = state.data.on;

  const flip = async () => {
    const ok = await confirmAction({
      title: on ? "إعادة العرفي كما كان في الإصدار 1.81؟" : "تفعيل طريقة العرفي الجديدة؟",
      body: on
        ? "سيقرأ حازم أسئلة العرفي كما في الإصدار 1.81 تمامًا."
        : "سيفهم حازم في العرفي ما يقوله العميل، أو ما يسأل عنه («ينفع ابوها؟»)، أو ما يتراجع عنه من كلامه («اخوها مش هيجي»)، ويجيب من قواعد المكتب نفسها. يمكنك إيقافها في أي وقت.",
      yes: on ? "نعم، أعِده" : "نعم، فعّله",
    });
    if (ok) set.mutate(!on);
  };

  return (
    <section aria-labelledby="testing-urf-case-said" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <h2 id="testing-urf-case-said" className="flex items-center gap-2 text-[16px] font-bold">
        <Icon name="alert-circle" className="size-4 text-gold-ink" />
        طريقة العرفي الجديدة
      </h2>
      <p className="text-[13px] text-ink-soft">
        «ينفع ابوها؟»، «ينفع أجيب الشهود؟»، «اخوها مش هيجي»: يجيب حازم من قواعد المكتب ولا يحوّل المحادثة إلى المكتب. عند إيقافها: يعمل العرفي كما في الإصدار 1.81 تمامًا.
      </p>
      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <span className={`rounded-full px-3 py-1 text-[13px] font-bold ${on ? "bg-green-bg text-green-ink" : "bg-grey-bg text-ink-soft"}`}>{on ? "مفعّلة" : "متوقفة"}</span>
        <Button variant={on ? "secondary" : "primary"} busy={set.isPending} onClick={() => void flip()}>
          {on ? "إيقاف" : "تفعيل"}
        </Button>
      </div>
      {set.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(set.error)}</p> : null}
    </section>
  );
}

/**
 * v1.61: when the client asks about the office's address or hours, Hazem adds ONE sentence that answers his question before the
 * approved text (the address, the map and the hours stay exactly as written). Off is the approved text alone.
 */
function InfoWriter() {
  const qc = useQueryClient();
  const state = useQuery({ queryKey: ["testing", "info-writer"], queryFn: testing.infoWriter, retry: false });
  const set = useMutation({
    mutationFn: testing.setInfoWriter,
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ["testing", "info-writer"] });
      toast(r.on ? "تم تفعيل جملة الرد على أسئلة المكتب ✅" : "عادت ردود العنوان والمواعيد كما كانت ✅");
    },
  });
  if (state.isError && !(state.error instanceof ApiError && state.error.status === 404)) {
    return <ErrorState message={errorMessage(state.error)} onRetry={() => void state.refetch()} />;
  }
  if (!state.data) return null;
  const on = state.data.on;

  const flip = async () => {
    const ok = await confirmAction({
      title: on ? "إعادة ردود العنوان والمواعيد كما كانت؟" : "تفعيل جملة الرد على أسئلة المكتب؟",
      body: on
        ? "سيُرسل العنوان والمواعيد نصًا جاهزًا فقط، دون جملة قبلهما."
        : "عندما يسأل العميل عن عنوان المكتب أو مواعيده، سيجيب حازم عن سؤاله في جملة واحدة، ثم يرسل العنوان والخريطة والمواعيد كما هي حرفيًا. يمكنك إيقافها في أي وقت.",
      yes: on ? "نعم، أعِده" : "نعم، فعّله",
    });
    if (ok) set.mutate(!on);
  };

  return (
    <section aria-labelledby="testing-info-writer" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <h2 id="testing-info-writer" className="flex items-center gap-2 text-[16px] font-bold">
        <Icon name="alert-circle" className="size-4 text-gold-ink" />
        جملة الرد على أسئلة المكتب
      </h2>
      <p className="text-[13px] text-ink-soft">
        جملة تجيب عن سؤال العميل قبل العنوان والمواعيد (مثلًا «فيه فرع في إسكندرية؟»). ولا يتغيّر النص الجاهز نفسه.
      </p>
      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <span className={`rounded-full px-3 py-1 text-[13px] font-bold ${on ? "bg-green-bg text-green-ink" : "bg-grey-bg text-ink-soft"}`}>{on ? "مفعّلة" : "متوقفة"}</span>
        <Button variant={on ? "secondary" : "primary"} busy={set.isPending} onClick={() => void flip()}>
          {on ? "إيقاف" : "تفعيل"}
        </Button>
      </div>
      {set.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(set.error)}</p> : null}
    </section>
  );
}

/**
 * v1.52 (phase 1): Hazem reads the whole chat and the client's stance («بسأل بس»، «غالي»، أكتر من سؤال، السن بالحروف).
 * Off is Hazem exactly as before, so the owner can go back in a second.
 */
function Understanding() {
  const qc = useQueryClient();
  const state = useQuery({ queryKey: ["testing", "understanding"], queryFn: testing.understanding, retry: false });
  const set = useMutation({
    mutationFn: testing.setUnderstanding,
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ["testing", "understanding"] });
      toast(r.on ? "تم تفعيل الفهم الجديد ✅" : "عاد حازم كما كان ✅");
    },
  });
  if (state.isError && !(state.error instanceof ApiError && state.error.status === 404)) {
    return <ErrorState message={errorMessage(state.error)} onRetry={() => void state.refetch()} />;
  }
  if (!state.data) return null;
  const on = state.data.on;

  const flip = async () => {
    const ok = await confirmAction({
      title: on ? "إعادة حازم كما كان؟" : "تفعيل الفهم الجديد؟",
      body: on
        ? "سيعود حازم إلى فهم الرسالة كما كان تمامًا."
        : "سيقرأ حازم المحادثة كاملة، ويفهم إن كان العميل يستفسر فقط أو لا يريد المتابعة أو يرى السعر مرتفعًا، ويجيب عن جميع الأسئلة الواردة في الرسالة، ويفهم السن المكتوب بالحروف. يمكنك إيقافه في أي وقت.",
      yes: on ? "نعم، أعِده" : "نعم، فعّله",
    });
    if (ok) set.mutate(!on);
  };

  return (
    <section aria-labelledby="testing-understanding" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <h2 id="testing-understanding" className="flex items-center gap-2 text-[16px] font-bold">
        <Icon name="alert-circle" className="size-4 text-gold-ink" />
        الفهم الجديد (المرحلة 1)
      </h2>
      <p className="text-[13px] text-ink-soft">
        يقرأ حازم المحادثة كاملة ويفهم موقف العميل. إذا حدثت أي مشكلة، فأوقفه ليعود حازم كما كان خلال ثانية.
      </p>
      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <span className={`rounded-full px-3 py-1 text-[13px] font-bold ${on ? "bg-green-bg text-green-ink" : "bg-grey-bg text-ink-soft"}`}>{on ? "مفعّل" : "متوقف"}</span>
        <Button variant={on ? "secondary" : "primary"} busy={set.isPending} onClick={() => void flip()}>
          {on ? "إيقاف" : "تفعيل"}
        </Button>
      </div>
      {set.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(set.error)}</p> : null}
    </section>
  );
}

/**
 * v1.34 (B-09.6): until Meta approves urgent_alert, the lawyer's (and the manager's) alert can go as a normal message when
 * that number wrote to the bot in the last 24 hours. Off by default; the healthcheck fails if it is left on in production.
 */
function UrgentWindow() {
  const qc = useQueryClient();
  const state = useQuery({ queryKey: ["testing", "urgent-window"], queryFn: testing.urgentWindow, retry: false });
  const set = useMutation({
    mutationFn: testing.setUrgentWindow,
    onSuccess: (r) => {
      void qc.invalidateQueries({ queryKey: ["testing", "urgent-window"] });
      toast(r.on ? "سيصل تنبيه العاجل رسالةً عادية ضمن نافذة الـ 24 ساعة ✅" : "عاد تنبيه العاجل إلى القالب فقط ✅");
    },
  });
  // 404: not the test environment, the switch is simply not there
  if (state.isError && !(state.error instanceof ApiError && state.error.status === 404)) {
    return <ErrorState message={errorMessage(state.error)} onRetry={() => void state.refetch()} />;
  }
  if (!state.data) return null;
  const on = state.data.on;

  const flip = async () => {
    const ok = await confirmAction({
      title: on ? "إعادة التنبيه إلى القالب فقط؟" : "تفعيل تنبيه العاجل ضمن نافذة الـ 24 ساعة؟",
      body: on
        ? "سيُرسل تنبيه المستشار والمدير بقالب urgent_alert فقط، كما في DECISIONS. وإلى أن توافق Meta عليه، سيصل في اللوحة والبريد الإلكتروني فقط."
        : "إذا أرسل المستشار أو المدير أي رسالة إلى البوت خلال آخر 24 ساعة، فسيصله التنبيه رسالةً عادية بالنص نفسه مع زر «شوف التحويل». وفي غير ذلك، يبقى القالب كما هو. هذا للتجربة فقط: أوقفه قبل الإطلاق.",
      yes: on ? "نعم، أعِده" : "نعم، فعّله",
    });
    if (ok) set.mutate(!on);
  };

  return (
    <section aria-labelledby="testing-urgent" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <h2 id="testing-urgent" className="flex items-center gap-2 text-[16px] font-bold">
        <Icon name="alert-circle" className="size-4 text-gold-ink" />
        تنبيه العاجل ضمن نافذة الـ 24 ساعة
      </h2>
      <p className="text-[13px] text-ink-soft">
        إلى أن توافق Meta على قالب تنبيه المستشار، يتيح هذا الخيار وصول التنبيه رسالةً عادية إذا كان المستشار أو المدير قد راسل البوت خلال آخر 24 ساعة.
        للتجربة فقط.
      </p>
      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <span className={`rounded-full px-3 py-1 text-[13px] font-bold ${on ? "bg-green-bg text-green-ink" : "bg-grey-bg text-ink-soft"}`}>{on ? "مفعّل" : "متوقف"}</span>
        <Button variant={on ? "secondary" : "primary"} busy={set.isPending} onClick={() => void flip()}>
          {on ? "إيقاف" : "تفعيل"}
        </Button>
      </div>
      {set.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(set.error)}</p> : null}
    </section>
  );
}

/** «…0589» → «0589»: the end of the number, as the office says it. */
const ending = (masked: string) => masked.replace(/^\*+/, "");

/**
 * «أدوات التجربة» (the owner, 2026-10-30): Hazem forgets one of his test numbers, so a conversation is tested from the welcome
 * again (the same as scripts/linux/forget-number.sh). The manager and the lawyer; the test environment only (the API answers 404
 * anywhere else, and this page says so); only the numbers Hazem answers while testing (hazem.recipients), never a client's.
 * As «liquid glass»: a glass header, and each number a glass row.
 */
export function TestingPage() {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["testing", "numbers"], queryFn: testing.numbers, retry: false });
  const forget = useMutation({
    mutationFn: async (items: TestNumber[]) => {
      const done: Forgot[] = [];
      const failed: { masked: string; error: unknown }[] = [];
      // one at a time, in order; a number that fails does not stop the next (the ones before it are already forgotten)
      for (const n of items) {
        try { done.push(await testing.forget(n.key)); } catch (error) { failed.push({ masked: n.masked, error }); }
      }
      return { done, failed };
    },
    // whatever happened, the list shows what is left
    onSettled: () => void qc.invalidateQueries(),
    onSuccess: ({ done, failed }) => {
      for (const f of failed) toast(`لم ينسَ حازم الرقم المنتهي بـ ${ending(f.masked)}: ${errorMessage(f.error)}`, "bad");
      for (const r of done) {
        toast(`نسي حازم الرقم المنتهي بـ ${ending(r.masked)} ✅`);
        if (r.chatwoot === "failed") toast(`ما زالت محادثته القديمة في Chatwoot: احذفها من هناك إذا أردت`, "bad");
        if (r.calendar_by_hand.length > 0) toast(`مواعيد في تقويم المستشار لم تُحذف (${r.calendar_by_hand.length}): احذفها يدويًا (${r.calendar_by_hand.map((t) => cairoWhen(t)).join("، ")})`, "bad");
      }
    },
  });

  const ask = async (items: TestNumber[]) => {
    const which = items.length === 1 ? `الرقم المنتهي بـ ${ending(items[0]!.masked)}` : `الأرقام (${items.map((n) => ending(n.masked)).join("، ")})`;
    const ok = await confirmAction({
      title: items.length === 1 ? "هل ينسى حازم هذا الرقم؟" : "هل ينسى حازم جميع أرقام التجربة؟",
      body: `سينسى حازم كل ما يخص ${which}: المحادثات، والاسم، والطلبات، والحجوزات ومواعيدها في تقويم المستشار، والمحادثة في Chatwoot. وستبدأ أول رسالة تالية منه من الترحيب. لا يمكن التراجع عن هذا الإجراء.`,
      yes: items.length === 1 ? "نعم، انسَه" : "نعم، انسَهم",
      danger: true,
    });
    if (ok) forget.mutate(items);
  };

  const off = list.error instanceof ApiError && list.error.status === 404;

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-orange/10 blur-[100px]" />
      </div>

      <header className="relative">
        <div className="relative flex items-start gap-4">
          <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="settings" className="size-6" />
          </span>
          <div className="grid min-w-0 gap-1">
            <h1 className="text-[26px] leading-tight font-bold">أدوات التجربة</h1>
          </div>
        </div>
      </header>

      {list.isPending ? <Loading /> : off ? (
        <Nothing title="هذه الأدوات مُعطَّلة" icon="lock">النظام ليس في وضع التجربة، لذلك لا يمكن لأحد حذف أي شيء من هنا.</Nothing>
      ) : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : list.data.numbers.length === 0 ? (
        <Nothing title="لا توجد أرقام تجربة" icon="message">لا توجد أرقام في قائمة الأرقام التي يردّ عليها حازم أثناء التجربة.</Nothing>
      ) : (
        <section aria-labelledby="testing-numbers" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
          <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/15 blur-3xl" />
          <h2 id="testing-numbers" className="flex items-center gap-2 text-[16px] font-bold">
            <Icon name="whatsapp" className="size-4 text-gold-ink" />
            أرقام التجربة
            <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2 py-0.5 text-[11px] font-bold text-gold-ink">{list.data.numbers.length}</span>
          </h2>
          <ul className="grid gap-2" aria-label="أرقام التجربة">
            {list.data.numbers.map((n) => (
              <li key={n.key} className="group relative flex flex-wrap items-center gap-3 overflow-hidden rounded-[16px] border border-line/60 bg-card/55 py-3 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_4px_14px_-8px_rgb(60_44_20/0.25)] transition hover:-translate-y-px hover:border-gold-line hover:bg-card/80 motion-reduce:transition-none motion-reduce:hover:translate-y-0">
                <span aria-hidden className="absolute inset-y-3 start-0 w-1 rounded-e-full bg-green" />
                <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-full bg-gradient-to-br from-green-bg to-card text-green-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                  <Icon name="whatsapp" className="size-5" />
                </span>
                <span className="grid min-w-0 flex-1 gap-0.5">
                  <span className="text-[11.5px] text-ink-soft">رقم تجربة</span>
                  <span className="font-semibold">
                    الرقم المنتهي بـ <span dir="ltr" className="tabular">{ending(n.masked)}</span>
                  </span>
                </span>
                <Button variant="danger" busy={forget.isPending && forget.variables?.length === 1 && forget.variables[0]?.key === n.key}
                  disabled={forget.isPending} onClick={() => void ask([n])}>
                  انسَ هذا الرقم
                </Button>
              </li>
            ))}
          </ul>
          {list.data.numbers.length > 1 ? (
            <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
              <Button variant="secondary" busy={forget.isPending && (forget.variables?.length ?? 0) > 1} disabled={forget.isPending}
                onClick={() => void ask(list.data.numbers)}>
                انسَ جميع الأرقام ({list.data.numbers.length})
              </Button>
            </div>
          ) : null}
          {forget.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(forget.error)}</p> : null}
        </section>
      )}
      {off ? null : <Understanding />}
      {off ? null : <InfoWriter />}
      {off ? null : <CaseSaid />}
      {off ? null : <UrgentWindow />}
    </div>
  );
}

function Nothing({ title, icon, children }: { title: string; icon: IconName; children: ReactNode }) {
  return (
    <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
      <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name={icon} className="size-7" />
      </span>
      <p className="text-[16px] font-bold">{title}</p>
      <p className="max-w-sm text-[13px] text-ink-soft">{children}</p>
    </div>
  );
}
