import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { cairoWhen } from "../../shared/lib/time";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, Empty, ErrorState, Loading } from "../../shared/ui/ui";

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
    <div className="grid gap-4">
      <h1 className="text-2xl">أدوات التجربة</h1>
      <p className="text-sm text-ink-soft">
        علشان تجرّب محادثة من الأول: حازم ينسى رقم من أرقام التجربة كأنه أول مرة يكلّمه. الرقم بيفضل في أرقام التجربة.
        الصفحة دي بتشتغل في وضع التجربة بس، وعلى أرقام التجربة بس.
      </p>
      {list.isPending ? <Loading /> : off ? (
        <Card><Empty title="الأدوات دي مقفولة" icon="alert">السيستم مش في وضع التجربة، فمحدش يقدر يمسح حاجة من هنا.</Empty></Card>
      ) : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : list.data.numbers.length === 0 ? (
        <Card><Empty title="مفيش أرقام تجربة" icon="message">مفيش أرقام في قايمة الأرقام اللي حازم بيرد عليها وهو في التجربة.</Empty></Card>
      ) : (
        <Card>
          <ul className="grid divide-y divide-line" aria-label="أرقام التجربة">
            {list.data.numbers.map((n) => (
              <li key={n.key} className="flex flex-wrap items-center gap-3 py-3">
                <Icon name="whatsapp" className="size-5 text-ink-soft" />
                <span className="flex-1 font-semibold">
                  الرقم اللي آخره <span dir="ltr" className="tabular">{ending(n.masked)}</span>
                </span>
                <Button variant="danger" busy={forget.isPending && forget.variables?.length === 1 && forget.variables[0] === n.key}
                  disabled={forget.isPending} onClick={() => void ask([n])}>
                  انسى الرقم ده
                </Button>
              </li>
            ))}
          </ul>
          {list.data.numbers.length > 1 ? (
            <div className="border-t border-line pt-3">
              <Button variant="secondary" busy={forget.isPending && (forget.variables?.length ?? 0) > 1} disabled={forget.isPending}
                onClick={() => void ask(list.data.numbers)}>
                انسى كل الأرقام ({list.data.numbers.length})
              </Button>
            </div>
          ) : null}
          {forget.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(forget.error)}</p> : null}
        </Card>
      )}
      {off ? null : <UrgentWindow />}
    </div>
  );
}
