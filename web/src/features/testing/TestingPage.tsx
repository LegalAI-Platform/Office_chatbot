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
};

/** «…0589» → «0589»: the end of the number, as the office says it. */
const ending = (masked: string) => masked.replace(/^\*+/, "");

// how a conversation is tested from the start
const STEPS: { icon: IconName; title: string; text: string }[] = [
  { icon: "phone", title: "اختار الرقم", text: "من أرقام التجربة اللي تحت." },
  { icon: "refresh", title: "اضغط «انسى الرقم ده»", text: "وأكّد: حازم بينسى كل حاجة عنه." },
  { icon: "whatsapp", title: "ابعت رسالة من الرقم", text: "حازم هيرد كأنها أول مرة، من الترحيب." },
];

/**
 * «أدوات التجربة» (the owner, 2026-10-30): Hazem forgets one of his test numbers, so a conversation is tested from the welcome
 * again (the same as scripts/linux/forget-number.sh). The manager and the lawyer; the test environment only (the API answers 404
 * anywhere else, and this page says so); only the numbers Hazem answers while testing (hazem.recipients), never a client's.
 * As «liquid glass»: a header saying what the tool is for with its three steps, a warning that it cannot be undone, and each
 * number a glass row.
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

      <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
        <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
        <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
        <div className="relative grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(0,1.35fr)] xl:items-center">
          <div className="flex items-start gap-4">
            <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
              <Icon name="settings" className="size-7" />
            </span>
            <div className="grid min-w-0 gap-1">
              <h1 className="text-[26px] leading-tight font-bold">أدوات التجربة</h1>
              <p className="text-[14px] leading-relaxed text-ink-soft">
                علشان تجرّب محادثة من الأول: حازم ينسى رقم من أرقام التجربة كأنه أول مرة يكلّمه. الرقم بيفضل في أرقام التجربة.
                الصفحة دي بتشتغل في <strong className="text-ink">وضع التجربة بس</strong>، وعلى أرقام التجربة بس.
              </p>
            </div>
          </div>
          <ol className="grid gap-2 sm:grid-cols-3" aria-label="إزاي تجرّب محادثة من الأول">
            {STEPS.map((s, i) => (
              <li key={s.title} className="relative flex items-start gap-3 rounded-[16px] border border-line/60 bg-card/55 p-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
                <span className="tabular grid size-7 shrink-0 place-items-center rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] text-[13px] font-bold text-[#2a2117] shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                  {i + 1}
                </span>
                <span className="grid gap-0.5">
                  <span className="text-[13.5px] font-bold">{s.title}</span>
                  <span className="text-[12px] leading-snug text-ink-soft">{s.text}</span>
                </span>
              </li>
            ))}
          </ol>
        </div>
      </header>

      {list.isPending ? <Loading /> : off ? (
        <Nothing title="الأدوات دي مقفولة" icon="lock">السيستم مش في وضع التجربة، فمحدش يقدر يمسح حاجة من هنا.</Nothing>
      ) : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : list.data.numbers.length === 0 ? (
        <Nothing title="مفيش أرقام تجربة" icon="message">مفيش أرقام في قايمة الأرقام اللي حازم بيرد عليها وهو في التجربة.</Nothing>
      ) : (
        <section aria-labelledby="testing-numbers" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
          <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/15 blur-3xl" />
          <header className="grid gap-0.5">
            <h2 id="testing-numbers" className="flex items-center gap-2 text-[16px] font-bold">
              <Icon name="whatsapp" className="size-4 text-gold-ink" />
              أرقام التجربة
              <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2 py-0.5 text-[11px] font-bold text-gold-ink">{list.data.numbers.length}</span>
            </h2>
            <p className="text-[12.5px] text-ink-soft">الأرقام اللي حازم بيرد عليها وهو في التجربة. بيظهر آخر الرقم بس.</p>
          </header>
          <p className="flex items-start gap-2 rounded-[14px] border border-red-ink/25 bg-red-bg/60 px-3.5 py-2.5 text-[13px] text-red-ink">
            <Icon name="alert" className="mt-0.5 size-4 shrink-0" />
            <span>النسيان <strong>مايترجعش</strong>: المحادثات والاسم والطلبات والحجوزات ومواعيدها بتتمسح خالص.</span>
          </p>
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
              <p className="text-[12.5px] text-ink-soft">بينساهم واحد ورا التاني، عشان تبدأ التجربة كلها من الأول.</p>
            </div>
          ) : null}
          {forget.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(forget.error)}</p> : null}
        </section>
      )}
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
