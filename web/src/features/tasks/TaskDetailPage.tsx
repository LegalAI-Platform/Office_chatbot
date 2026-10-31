import { useState } from "react";
import { Link, useParams } from "react-router";
import { openChatwoot } from "../../shared/api/chatwoot";
import { errorMessage } from "../../shared/api/client";
import type { ClientWord, TaskDetail } from "../../shared/api/types";
import { CLIENT_TYPES, label, matterLabel, MEDIA_TYPES, SOURCES, TASK_STATES, TASK_TYPES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { phoneOf } from "../../shared/ui/caseCard";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { useAssign, useStaff, useTask, useTaskAction, type TaskAction } from "./api";
import { isUrgent } from "./TasksPage";

export function TaskDetailPage() {
  const { id = "" } = useParams();
  const task = useTask(id);

  return (
    <div className="grid gap-4">
      <Link to="/tasks" className="inline-flex w-fit items-center gap-1 text-sm text-ink-soft hover:text-ink">
        <Icon name="back" className="size-4" />
        المهام
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

function Detail({ task }: { task: TaskDetail }) {
  const open = task.state !== "done" && task.state !== "cancelled";
  return (
    <>
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="grid gap-1">
          <h1 className="text-2xl">{task.client.name ?? "رقم من غير اسم"}</h1>
          <p className="text-sm text-ink-soft">
            {label(TASK_TYPES, task.type)} · {task.state === "overdue" ? "كان المفروض " : "المفروض "}
            <span className="tabular">{cairoWhen(task.due_at)}</span>
          </p>
        </div>
        <span className="flex gap-1.5">
          {isUrgent(task) && open ? <Chip tone="urgent">عاجل</Chip> : null}
          <Chip tone={task.state}>{label(TASK_STATES, task.state)}</Chip>
        </span>
      </header>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card>
          <h2 className="text-lg">كلام العميل</h2>
          <p className="text-xs text-ink-soft">
            نوع الموضوع: <strong className="text-ink">{matterLabel(task.matter_type, task.matter_subtype)}</strong>. ده
            تصنيف حازم، فاقرا كلام العميل نفسه قبل ما تعتمد عليه.
          </p>
          <ClientWords taskId={task.id} words={task.client_words} />
        </Card>

        <div className="grid content-start gap-4">
          <Card>
            <h2 className="text-lg">العميل</h2>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
              <dt className="text-ink-soft">الرقم</dt>
              <dd className="tabular font-medium" dir="ltr" style={{ textAlign: "right" }}>{phoneOf(task.client.phone) ?? "من غير رقم"}</dd>
              <dt className="text-ink-soft">النوع</dt>
              <dd className="font-medium">{label(CLIENT_TYPES, task.client.client_type) || "—"}</dd>
              <dt className="text-ink-soft">جه منين</dt>
              <dd className="font-medium">{label(SOURCES, task.client.source) || "مش معروف"}</dd>
              <dt className="text-ink-soft">حازم</dt>
              <dd className="font-medium">{hazemState(task.client.conversation_state)}</dd>
            </dl>
          </Card>
          <Actions task={task} />
        </div>
      </div>
    </>
  );
}

const hazemState = (s?: string) =>
  s === "human_handoff" ? "ساكت، موظف ماسك المحادثة" : s === "muted" ? "متكتم" : s === "ignored" ? "جروب، متجاهل" : "شغال";

function ClientWords({ taskId, words }: { taskId: string; words: ClientWord[] }) {
  if (words.length === 0) return <p className="text-sm text-ink-soft">مفيش كلام متسجل للمهمة دي.</p>;
  return (
    <ol className="grid gap-2">
      {words.map((w, i) => {
        const text = w.text ?? w.transcript ?? w.caption;
        const media = w.type !== "text" ? label(MEDIA_TYPES, w.type) : null;
        return (
          <li key={i} className="grid gap-1 rounded-[var(--radius-control)] border-s-[3px] border-s-brass bg-muted px-3.5 py-2.5">
            {media ? (
              <span className="text-xs font-semibold text-ink-soft">
                {media}
                {w.transcript ? " · التفريغ" : ""}
              </span>
            ) : null}
            {w.media_status === "stored" ? (
              // TASK 07c: opened by the api for whoever may see this task; every opening is recorded.
              <a href={`/api/panel/tasks/${taskId}/files/${i}`} target="_blank" rel="noopener noreferrer"
                className="inline-flex w-fit items-center gap-1.5 text-sm font-semibold text-gold-ink hover:underline">
                <Icon name="document" className="size-4" />
                افتح {media ?? "الملف"}
              </a>
            ) : null}
            {text ? <p className="font-display text-lg leading-relaxed whitespace-pre-wrap">{text}</p> : null}
            {w.sent_at ? <span className="tabular text-xs text-ink-soft">{cairoWhen(w.sent_at)}</span> : null}
          </li>
        );
      })}
    </ol>
  );
}

const ACTIONS: { action: TaskAction; key: "start" | "done" | "cancel"; text: string; variant: "primary" | "secondary" | "danger" }[] = [
  { action: "start", key: "start", text: "هاشتغل عليها", variant: "primary" },
  { action: "done", key: "done", text: "خلصت", variant: "primary" },
  { action: "cancel", key: "cancel", text: "إلغاء المهمة", variant: "danger" },
];

function Actions({ task }: { task: TaskDetail }) {
  const act = useTaskAction(task.id);
  const assign = useAssign(task.id);
  const staff = useStaff(task.can.assign);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [chat, setChat] = useState<{ busy: boolean; error?: unknown }>({ busy: false });
  const available = ACTIONS.filter((a) => task.can[a.key]);

  return (
    <Card>
      <h2 className="text-lg">المهمة</h2>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
        <dt className="text-ink-soft">الموظف</dt>
        <dd className="font-medium">{task.staff?.name ?? "مش متعيّنة"}</dd>
        <dt className="text-ink-soft">اتفتحت</dt>
        <dd className="tabular font-medium">{cairoWhen(task.created_at)}</dd>
      </dl>

      {task.can.assign ? (
        <label className="grid gap-1.5 text-sm">
          <span className="font-medium">حوّل المهمة لـ</span>
          <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 text-ink" value={task.staff?.id ?? ""}
            disabled={assign.isPending || staff.isPending}
            onChange={(e) => assign.mutate(e.target.value || null)}>
            <option value="">مش متعيّنة</option>
            {staff.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </label>
      ) : null}

      {available.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {available.map((a) =>
            a.action === "cancel" ? (
              confirmCancel ? (
                <span key="cancel" className="flex flex-wrap items-center gap-2">
                  <span className="text-sm">متأكد إنك عايز تلغيها؟</span>
                  <Button variant="danger" busy={act.isPending} onClick={() => act.mutate("cancel")}>أيوه، الغيها</Button>
                  <Button variant="ghost" onClick={() => setConfirmCancel(false)}>لأ</Button>
                </span>
              ) : (
                <Button key="cancel" variant="ghost" onClick={() => setConfirmCancel(true)}>{a.text}</Button>
              )
            ) : (
              <Button key={a.action} variant={a.variant} busy={act.isPending && act.variables === a.action}
                onClick={() => act.mutate(a.action)}>
                <Icon name={a.action === "start" ? "play" : "check"} className="size-4" />
                {a.text}
              </Button>
            ),
          )}
        </div>
      ) : null}

      {act.isError || assign.isError ? (
        <p className="text-sm text-bad" role="alert">{errorMessage(act.error ?? assign.error)}</p>
      ) : null}

      <div className="grid gap-1 border-t border-line pt-3">
        <Button aria-describedby="chatwoot-note" busy={chat.busy} onClick={async () => {
          setChat({ busy: true });
          setChat({ busy: false, error: await openChatwoot(task.id) });
        }}>
          <Icon name="chat" className="size-4" />
          افتح المحادثة في Chatwoot
        </Button>
        <p id="chatwoot-note" className="text-xs text-ink-soft">بتفتح في تاب جديد وانت داخل على طول. ردك من هناك بيوصل للعميل على واتساب.</p>
        {chat.error ? <p className="text-sm text-bad" role="alert">{errorMessage(chat.error)}</p> : null}
      </div>
    </Card>
  );
}
