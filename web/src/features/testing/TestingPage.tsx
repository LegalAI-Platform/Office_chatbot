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
};

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
      toast(r.on ? "تنبيه العاجل هيوصل رسالة عادية جوه الـ 24 ساعة ✅" : "تنبيه العاجل رجع قالب بس ✅");
    },
  });
  if (!state.data) return null;
  const on = state.data.on;

  const flip = async () => {
    const ok = await confirmAction({
      title: on ? "ترجّع التنبيه قالب بس؟" : "تشغّل تنبيه العاجل جوه الـ 24 ساعة؟",
      body: on
        ? "تنبيه المستشار والمدير هيتبعت قالب urgent_alert بس، زي DECISIONS. ولحد ما Meta توافق عليه، هيوصل في اللوحة والإيميل بس."
        : "لو المستشار أو المدير بعت أي رسالة للبوت في آخر 24 ساعة، التنبيه هيوصله رسالة عادية بنفس الكلام وزرار «شوف التحويل». غير كده، القالب زي ما هو. ده للتجربة بس: اقفله قبل الإطلاق.",
      yes: on ? "أيوه، رجّعه" : "أيوه، شغّله",
    });
    if (ok) set.mutate(!on);
  };

  return (
    <Card>
      <h2 className="text-lg">تنبيه العاجل جوه الـ 24 ساعة</h2>
      <p className="text-sm text-ink-soft">
        لحد ما Meta توافق على قالب تنبيه المستشار، ده بيخلّي التنبيه يوصل رسالة عادية لو المستشار أو المدير كاتب للبوت في آخر 24 ساعة.
        للتجربة بس.
      </p>
      <div className="flex flex-wrap items-center gap-3 pt-2">
        <span className="font-semibold">{on ? "شغّال" : "مقفول"}</span>
        <Button variant={on ? "secondary" : "primary"} busy={set.isPending} onClick={() => void flip()}>
          {on ? "اقفله" : "شغّله"}
        </Button>
      </div>
      {set.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(set.error)}</p> : null}
    </Card>
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
    mutationFn: async (keys: string[]) => {
      const done: Forgot[] = [];
      for (const k of keys) done.push(await testing.forget(k)); // one at a time, in order
      return done;
    },
    onSuccess: (done) => {
      void qc.invalidateQueries();
      for (const r of done) {
        toast(`حازم نسي الرقم اللي آخره ${ending(r.masked)} ✅`);
        if (r.chatwoot === "failed") toast(`محادثته القديمة لسه في Chatwoot: امسحها من هناك لو عايز`, "bad");
        if (r.calendar_by_hand.length > 0) toast(`فيه ${r.calendar_by_hand.length} ميعاد في كالندر المستشار ما اتمسحش: امسحه بإيدك (${r.calendar_by_hand.map((t) => cairoWhen(t)).join("، ")})`, "bad");
      }
    },
  });

  const ask = async (items: TestNumber[]) => {
    const which = items.length === 1 ? `الرقم اللي آخره ${ending(items[0]!.masked)}` : `الأرقام (${items.map((n) => ending(n.masked)).join("، ")})`;
    const ok = await confirmAction({
      title: items.length === 1 ? "حازم ينسى الرقم ده؟" : "حازم ينسى كل أرقام التجربة؟",
      body: `حازم هينسى كل حاجة عن ${which}: المحادثات، والاسم، والطلبات، والحجوزات ومواعيدها في كالندر المستشار، والمحادثة في Chatwoot. أول رسالة جاية منه هتبدأ من الترحيب. ده مايترجعش.`,
      yes: items.length === 1 ? "أيوه، انساه" : "أيوه، انساهم",
      danger: true,
    });
    if (ok) forget.mutate(items.map((n) => n.key));
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
        <Nothing title="الأدوات دي مقفولة" icon="lock">السيستم مش في وضع التجربة، فمحدش يقدر يمسح حاجة من هنا.</Nothing>
      ) : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : list.data.numbers.length === 0 ? (
        <Nothing title="مفيش أرقام تجربة" icon="message">مفيش أرقام في قايمة الأرقام اللي حازم بيرد عليها وهو في التجربة.</Nothing>
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
                    الرقم اللي آخره <span dir="ltr" className="tabular">{ending(n.masked)}</span>
                  </span>
                </span>
                <Button variant="danger" busy={forget.isPending && forget.variables?.length === 1 && forget.variables[0] === n.key}
                  disabled={forget.isPending} onClick={() => void ask([n])}>
                  انسى الرقم ده
                </Button>
              </li>
            ))}
          </ul>
          {list.data.numbers.length > 1 ? (
            <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
              <Button variant="secondary" busy={forget.isPending && (forget.variables?.length ?? 0) > 1} disabled={forget.isPending}
                onClick={() => void ask(list.data.numbers)}>
                انسى كل الأرقام ({list.data.numbers.length})
              </Button>
            </div>
          ) : null}
          {forget.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(forget.error)}</p> : null}
        </section>
      )}
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
