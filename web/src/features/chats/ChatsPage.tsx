import { useState } from "react";
import { openChatwoot } from "../../shared/api/chatwoot";
import { errorMessage } from "../../shared/api/client";
import { Icon } from "../../shared/ui/Icon";
import { Button, PageHeader } from "../../shared/ui/ui";

/**
 * «المحادثات» (TASK 07b, T-24): the conversations live in Chatwoot. This page opens it already
 * signed in, and says what happens there.
 */
export function ChatsPage() {
  const [state, setState] = useState<{ busy: boolean; error?: unknown }>({ busy: false });

  return (
    <div className="grid gap-5">
      <PageHeader section="/chats" title="المحادثات" />
      <section className="surface grid justify-items-center gap-4 rounded-[var(--radius-card)] px-6 py-12 text-center">
        <span className="grid size-12 place-items-center rounded-full bg-accent-tint text-accent-ink">
          <Icon name="chat" className="size-6" />
        </span>
        <p className="max-w-md text-[14.5px] text-ink-soft">
          محادثات العملاء والرد عليهم في <strong className="text-ink">Chatwoot</strong>. بيفتح في تاب جديد وانت داخل على طول،
          من غير كلمة سر تانية.
        </p>
        <Button variant="primary" busy={state.busy} onClick={async () => {
          setState({ busy: true });
          setState({ busy: false, error: await openChatwoot() });
        }}>
          <Icon name="chat" className="size-4" />
          افتح المحادثات
        </Button>
        {state.error ? <p className="text-sm text-bad" role="alert">{errorMessage(state.error)}</p> : null}
        <ul className="grid max-w-md list-disc gap-1.5 ps-5 text-start text-[14px] text-ink-soft marker:text-ink-faint">
          <li>ردك بيوصل للعميل على واتساب، وحازم بيسكت في المحادثة دي.</li>
          <li>لما تخلص، اقفل المحادثة (Resolve)، وحازم يرجع يرد على العميل.</li>
          <li>الصور والملفات بتظهر نوعها بس، وبتتفتح من لوحة المكتب.</li>
        </ul>
      </section>
    </div>
  );
}
