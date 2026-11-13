import { useId, useState, type ReactNode } from "react";
import { Link, useLocation, useParams } from "react-router";
import { openChatwoot } from "../../shared/api/chatwoot";
import { errorMessage } from "../../shared/api/client";
import type { ClientWord, TaskDetail } from "../../shared/api/types";
import { CLIENT_TYPES, label, matterLabel, MEDIA_TYPES, SOURCES, TASK_STATES, TASK_TYPES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { phoneOf } from "../../shared/ui/caseCard";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Avatar, Button, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { useAssign, useStaff, useTask, useTaskAction, type TaskAction } from "./api";
import { isUrgent } from "./TasksPage";

/** One task as «liquid glass»: who and what at the top, the client's own words as big quotes, the buttons in one card on the side. */
export function TaskDetailPage() {
  const { id = "" } = useParams();
  const task = useTask(id);
  // back to the list as it was left (filters, search, page) when we came from it
  const from = (useLocation().state as { from?: unknown } | null)?.from;
  const back = typeof from === "string" && from.startsWith("?") ? `/tasks${from}` : "/tasks";

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Link to={back} className="liquid inline-flex min-h-9 w-fit items-center gap-1.5 rounded-full px-3.5 text-[13px] font-semibold text-ink-soft transition hover:text-ink">
        <Icon name="back" className="size-4" />
        رجوع لكل المهام
      </Link>
      {task.isPending ? (
        <Loading />
      ) : task.isError ? (
        <ErrorState message={errorMessage(task.error)} onRetry={() => void task.refetch()} />
      ) : (
        <Detail task={task.data} />
      )}
    </div>
  );
}

const LOOK: Record<string, string> = {
  urgent: "bg-red", overdue: "bg-orange", in_progress: "bg-gold", open: "bg-blue", done: "bg-green", cancelled: "bg-ink-soft/50",
};

function Detail({ task }: { task: TaskDetail }) {
  const open = task.state !== "done" && task.state !== "cancelled";
  const name = task.client.name ?? "رقم من غير اسم";
  const late = task.state === "overdue";
  return (
    <>
      <header className="relative">
        <span aria-hidden className={`absolute inset-y-6 start-0 w-1 rounded-e-full ${LOOK[isUrgent(task) && open ? "urgent" : task.state] ?? "bg-blue"}`} />
        <div className="relative">
          <div className="flex flex-wrap items-start gap-4">
            <Avatar name={name} className="size-14 shrink-0 text-[16px]" />
            <div className="grid min-w-0 flex-1 gap-1.5">
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="text-[26px] leading-tight font-bold">{name}</h1>
                {isUrgent(task) && open ? <Chip tone="urgent">عاجل</Chip> : null}
                <Chip tone={task.state}>{label(TASK_STATES, task.state)}</Chip>
              </div>
              <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[13.5px] text-ink-soft">
                <span className="inline-flex items-center gap-1.5">
                  <Icon name="tasks" className="size-4 text-gold-ink" />
                  <strong className="text-ink">{label(TASK_TYPES, task.type)}</strong>
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <Icon name="clock" className="size-4 text-gold-ink" />
                  {late ? "كان المفروض تخلص " : "المفروض تخلص "}
                  <span className={`tabular font-semibold ${late ? "text-red-ink" : "text-ink"}`}>{cairoWhen(task.due_at)}</span>
                </span>
              </p>
            </div>
          </div>
        </div>
      </header>

      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1.6fr)_minmax(19rem,1fr)]">
        <Section icon="message" title="كلام العميل" count={task.client_words.length}>
          <p className="rounded-[12px] border border-line/60 bg-card/50 px-3 py-2 text-[12.5px] leading-relaxed text-ink-soft">
            نوع الموضوع: <strong className="text-ink">{matterLabel(task.matter_type, task.matter_subtype)}</strong>. ده
            تصنيف حازم، فاقرا كلام العميل نفسه قبل ما تعتمد عليه.
          </p>
          <ClientWords taskId={task.id} words={task.client_words} />
        </Section>

        <div className="grid content-start gap-5 lg:sticky lg:top-4">
          <Actions task={task} />
          <Section icon="account" title="العميل">
            <dl className="grid gap-1.5">
              <Row icon="whatsapp" term="الرقم"><span className="tabular" dir="ltr">{phoneOf(task.client.phone) ?? "من غير رقم"}</span></Row>
              <Row icon="users" term="النوع">{label(CLIENT_TYPES, task.client.client_type) || "—"}</Row>
              <Row icon="trend-up" term="جه منين">{label(SOURCES, task.client.source) || "مش معروف"}</Row>
              <Row icon="chat" term="حازم">{hazemState(task.client.conversation_state)}</Row>
            </dl>
          </Section>
        </div>
      </div>
    </>
  );
}

/** A glass card with the home page's header: an icon tile, the title, a count. */
function Section({ icon, title, count, children }: { icon: IconName; title: string; count?: number; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="liquid relative isolate grid min-w-0 gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/20 blur-3xl" />
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <header className="flex items-center gap-2">
        <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_2px_6px_-2px_rgb(60_44_20/0.2)]">
          <Icon name={icon} className="size-4" />
        </span>
        <h2 id={id} className="text-[17px] font-bold">{title}</h2>
        {count ? <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2 py-0.5 text-[11px] font-bold text-gold-ink">{count}</span> : null}
      </header>
      {children}
    </section>
  );
}

function Row({ icon, term, children }: { icon: IconName; term: string; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-[12px] border border-line/50 bg-card/50 px-3 py-2">
      <dt className="flex w-24 shrink-0 items-center gap-1.5 text-[12.5px] text-ink-soft">
        <Icon name={icon} className="size-3.5" />
        {term}
      </dt>
      <dd className="min-w-0 text-[13.5px] font-semibold" style={{ textAlign: "start" }}>{children}</dd>
    </div>
  );
}

const hazemState = (s?: string) =>
  s === "human_handoff" ? "ساكت، موظف ماسك المحادثة" : s === "muted" ? "متكتم" : s === "ignored" ? "جروب، متجاهل" : "شغال";

const MEDIA_ICON: Record<string, IconName> = { image: "camera", video: "video", audio: "phone", document: "document" };

/** The client's messages as quotes, in the order they came, each with its file and time. */
function ClientWords({ taskId, words }: { taskId: string; words: ClientWord[] }) {
  if (words.length === 0) {
    return <p className="rounded-[14px] border border-dashed border-gold-line bg-card/40 px-4 py-6 text-center text-sm text-ink-soft">مفيش كلام متسجل للمهمة دي.</p>;
  }
  return (
    <ol className="grid gap-2.5">
      {words.map((w, i) => {
        const text = w.text ?? w.transcript ?? w.caption;
        const media = w.type !== "text" ? label(MEDIA_TYPES, w.type) : null;
        return (
          <li key={i} className="relative grid gap-1.5 overflow-hidden rounded-[16px] rounded-ss-[4px] border border-line/60 bg-card/65 py-3 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)]">
            <span aria-hidden className="absolute inset-y-3 start-0 w-1 rounded-e-full bg-gold" />
            {media ? (
              <span className="inline-flex w-fit items-center gap-1.5 rounded-full border border-line/70 bg-card/70 px-2.5 py-0.5 text-[12px] font-semibold text-ink-soft">
                <Icon name={MEDIA_ICON[w.type] ?? "document"} className="size-3.5" />
                {media}
                {w.transcript ? " · التفريغ" : ""}
              </span>
            ) : null}
            {text ? <p className="font-display text-[18px] leading-relaxed whitespace-pre-wrap">{text}</p> : null}
            <span className="flex flex-wrap items-center justify-between gap-2">
              {w.media_status === "stored" ? (
                // TASK 07c: opened by the api for whoever may see this task; every opening is recorded.
                <a href={`/api/panel/tasks/${taskId}/files/${i}`} target="_blank" rel="noopener noreferrer"
                  className="inline-flex min-h-8 w-fit items-center gap-1.5 rounded-full border border-gold-line bg-gold-tint px-3 text-[13px] font-semibold text-gold-ink transition hover:brightness-105">
                  <Icon name="document" className="size-4" />
                  افتح {media ?? "الملف"}
                </a>
              ) : <span />}
              {w.sent_at ? <span className="tabular text-[12px] text-ink-soft">{cairoWhen(w.sent_at)}</span> : null}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

// «إلغاء المهمة» is apart, at the bottom behind a «متأكد؟»
const ACTIONS: { action: Exclude<TaskAction, "cancel">; text: string }[] = [
  { action: "start", text: "هاشتغل عليها" },
  { action: "done", text: "خلصت" },
];

function Actions({ task }: { task: TaskDetail }) {
  const act = useTaskAction(task.id);
  const assign = useAssign(task.id);
  const staff = useStaff(task.can.assign);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [chat, setChat] = useState<{ busy: boolean; error?: unknown }>({ busy: false });
  const main = ACTIONS.filter((a) => task.can[a.action]);
  const cancel = task.can.cancel;
  // «خلصت» is the step after «هاشتغل عليها»: while both show, only the first is gold
  const first = main[0]?.action;

  return (
    <Section icon="play" title="المهمة">
      <dl className="grid gap-1.5">
        <Row icon={task.staff ? "account" : "user-plus"} term="الموظف">
          {task.staff?.name ?? <span className="text-orange-ink">مش متعيّنة</span>}
        </Row>
        <Row icon="clock" term="اتفتحت"><span className="tabular">{cairoWhen(task.created_at)}</span></Row>
      </dl>

      {main.length > 0 ? (
        <div className="grid gap-2">
          {main.map((a) => (
            <Button key={a.action} variant={a.action === first ? "primary" : "secondary"} busy={act.isPending && act.variables === a.action}
              className="min-h-12 w-full text-[15px]" onClick={() => { assign.reset(); act.mutate(a.action); }}>
              <Icon name={a.action === "start" ? "play" : "check"} className="size-4" />
              {a.text}
            </Button>
          ))}
        </div>
      ) : (
        <p className="flex items-start gap-2 rounded-[12px] bg-green-bg px-3 py-2.5 text-[13px] text-green-ink">
          <Icon name="check-circle" className="mt-0.5 size-4 shrink-0" />
          {task.state === "done" ? "المهمة دي خلصت. مفيش حاجة تانية تتعمل فيها." : task.state === "cancelled" ? "المهمة دي اتلغت. مفيش حاجة تتعمل فيها." : "مفيش حاجة تقدر تعملها في المهمة دي من حسابك."}
        </p>
      )}

      {/* starting one clears the other's error, so this is always the last one that ran */}
      {act.isError || assign.isError ? (
        <p className="text-sm text-bad" role="alert">{errorMessage(act.isError ? act.error : assign.error)}</p>
      ) : null}

      <div className="grid gap-1 border-t border-line/70 pt-3">
        <Button aria-describedby="chatwoot-note" busy={chat.busy} className="w-full" onClick={async () => {
          setChat({ busy: true });
          setChat({ busy: false, error: await openChatwoot(task.id) });
        }}>
          <Icon name="chat" className="size-4" />
          افتح المحادثة في Chatwoot
        </Button>
        <p id="chatwoot-note" className="text-xs text-ink-soft">بتفتح في تاب جديد وانت داخل على طول. ردك من هناك بيوصل للعميل على واتساب.</p>
        {chat.error ? <p className="text-sm text-bad" role="alert">{errorMessage(chat.error)}</p> : null}
      </div>

      {task.can.assign ? (
        <div className="grid gap-1 border-t border-line/70 pt-3">
          <label className="grid gap-1.5 text-sm">
            <span className="flex items-center gap-1.5 font-semibold"><Icon name="user-plus" className="size-4 text-gold-ink" />حوّل المهمة لـ</span>
            <select className="min-h-11 rounded-[12px] border border-line-strong bg-card/70 px-3 text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.4)] focus:border-gold" value={task.staff?.id ?? ""}
              disabled={assign.isPending || staff.isPending}
              onChange={(e) => { act.reset(); assign.mutate(e.target.value || null); }}>
              <option value="">مش متعيّنة</option>
              {staff.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </label>
        </div>
      ) : null}

      {cancel ? (
        <div className="grid gap-2 border-t border-line/70 pt-3">
          {confirmCancel ? (
            <div className="grid gap-2 rounded-[12px] border border-red-ink/30 bg-red-bg p-3">
              <span className="text-sm font-semibold text-red-ink">متأكد إنك عايز تلغيها؟</span>
              <span className="flex flex-wrap gap-2">
                <Button variant="danger" busy={act.isPending} onClick={() => { assign.reset(); act.mutate("cancel"); }}>أيوه، الغيها</Button>
                <Button variant="ghost" onClick={() => setConfirmCancel(false)}>لأ</Button>
              </span>
            </div>
          ) : (
            <Button variant="ghost" className="w-fit text-red-ink" onClick={() => setConfirmCancel(true)}>
              <Icon name="x" className="size-4" />
              إلغاء المهمة
            </Button>
          )}
        </div>
      ) : null}
    </Section>
  );
}
