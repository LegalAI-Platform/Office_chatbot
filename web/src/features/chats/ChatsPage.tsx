import { useState } from "react";
import { openChatwoot } from "../../shared/api/chatwoot";
import { errorMessage } from "../../shared/api/client";
import { Icon } from "../../shared/ui/Icon";
import { OwnerImg } from "../../shared/ui/Owner";
import { Button } from "../../shared/ui/ui";

/**
 * «المحادثات» (TASK 07b, T-24): the conversations live in Chatwoot. This page opens it already
 * signed in, and says what happens there.
 */
export function ChatsPage() {
  const [state, setState] = useState<{ busy: boolean; error?: unknown }>({ busy: false });

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">المحادثات</h1>
      <section className="surface relative grid justify-items-center gap-4 overflow-hidden rounded-[var(--radius-card)] px-6 py-12 text-center">
        <OwnerImg name="sidebar_art" className="pointer-events-none absolute bottom-0 left-0 w-44 opacity-25" />
        <span className="grid size-14 place-items-center rounded-2xl bg-side text-gold">
          <Icon name="chat" className="size-7" />
        </span>
        <p className="max-w-md text-sm text-ink-soft">
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
        <ul className="grid max-w-md gap-1.5 text-start text-sm text-ink-soft">
          <li>• ردك بيوصل للعميل على واتساب، وحازم بيسكت في المحادثة دي.</li>
          <li>• لما تخلص، اقفل المحادثة (Resolve)، وحازم يرجع يرد على العميل.</li>
          <li>• الصور والملفات بتظهر نوعها بس، وبتتفتح من لوحة المكتب.</li>
        </ul>
      </section>
    </div>
  );
}
