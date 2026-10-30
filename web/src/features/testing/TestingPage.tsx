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
};

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
        if (r.calendar_by_hand.length > 0) toast(`فيه ${r.calendar_by_hand.length} ميعاد في كالندر الأستاذ ما اتمسحش: امسحه بإيدك (${r.calendar_by_hand.map((t) => cairoWhen(t)).join("، ")})`, "bad");
      }
    },
  });

  const ask = async (items: TestNumber[]) => {
    const which = items.length === 1 ? `الرقم اللي آخره ${ending(items[0]!.masked)}` : `الأرقام (${items.map((n) => ending(n.masked)).join("، ")})`;
    const ok = await confirmAction({
      title: items.length === 1 ? "حازم ينسى الرقم ده؟" : "حازم ينسى كل أرقام التجربة؟",
      body: `حازم هينسى كل حاجة عن ${which}: المحادثات، والاسم، والطلبات، والحجوزات ومواعيدها في كالندر الأستاذ، والمحادثة في Chatwoot. أول رسالة جاية منه هتبدأ من الترحيب. ده مايترجعش.`,
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
    </div>
  );
}
