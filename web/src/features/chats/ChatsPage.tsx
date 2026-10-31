import { useState } from "react";
import { openChatwoot } from "../../shared/api/chatwoot";
import { errorMessage } from "../../shared/api/client";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { OwnerImg } from "../../shared/ui/Owner";
import { Button } from "../../shared/ui/ui";

// what happens in Chatwoot, as the three things a new person needs to know before answering a client
const STEPS: { icon: IconName; title: string; text: string }[] = [
  { icon: "whatsapp", title: "انت اللي بترد", text: "ردك بيوصل للعميل على واتساب، وحازم بيسكت في المحادثة دي." },
  { icon: "check-circle", title: "لما تخلّص اقفلها", text: "لما تخلص، اقفل المحادثة (Resolve)، وحازم يرجع يرد على العميل." },
  { icon: "document", title: "الصور والملفات", text: "الصور والملفات بتظهر نوعها بس، وبتتفتح من لوحة المكتب." },
];

/**
 * «المحادثات» (TASK 07b, T-24): the conversations live in Chatwoot. This page opens it already
 * signed in, and says what happens there. As «liquid glass»: a header saying what the page is for, one glass card with the
 * button, and the three things to know as numbered glass steps.
 */
export function ChatsPage() {
  const [state, setState] = useState<{ busy: boolean; error?: unknown }>({ busy: false });

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-green/10 blur-[100px]" />
      </div>

      <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
        <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
        <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
        <div className="relative flex items-start gap-4">
          <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="chat" className="size-7" />
          </span>
          <div className="grid min-w-0 gap-1">
            <h1 className="text-[26px] leading-tight font-bold">المحادثات</h1>
            <p className="max-w-2xl text-[14px] leading-relaxed text-ink-soft">
              هنا بتكلّم العميل <strong className="text-ink">بنفسك</strong> بدل حازم: لما مهمة تقول إن العميل عايز يكلّم موظف، أو عايز تكمّل
              معاه كلام. المحادثات نفسها في برنامج اسمه Chatwoot.
            </p>
          </div>
        </div>
      </header>

      <section aria-label="افتح Chatwoot" className="liquid relative isolate grid justify-items-center gap-4 overflow-hidden rounded-[22px] px-6 py-10 text-center">
        <OwnerImg name="sidebar_art" className="pointer-events-none absolute bottom-0 left-0 -z-10 hidden w-44 opacity-20 sm:block" />
        <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-56 rounded-full bg-gold/20 blur-3xl" />
        <span className="grid size-16 place-items-center rounded-[20px] bg-gradient-to-br from-green-bg to-card text-green-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(7_138_93/0.5)]">
          <Icon name="whatsapp" className="size-8" />
        </span>
        <p className="max-w-md text-[14px] leading-relaxed text-ink-soft">
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
      </section>

      <section aria-labelledby="chats-know" className="grid gap-3">
        <h2 id="chats-know" className="flex items-center gap-2 px-1 text-[16px] font-bold">
          <Icon name="info" className="size-4 text-gold-ink" />
          قبل ما ترد، اعرف 3 حاجات
        </h2>
        <ol className="grid gap-2.5 md:grid-cols-3">
          {STEPS.map((s, i) => (
            <li key={s.title} className="liquid relative flex items-start gap-3 rounded-[18px] p-4">
              <span className="tabular grid size-7 shrink-0 place-items-center rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] text-[13px] font-bold text-[#2a2117] shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                {i + 1}
              </span>
              <span className="grid gap-1">
                <span className="flex items-center gap-1.5 text-[14px] font-bold">
                  <Icon name={s.icon} className="size-4 text-gold-ink" />
                  {s.title}
                </span>
                <span className="text-[13px] leading-snug text-ink-soft">{s.text}</span>
              </span>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
