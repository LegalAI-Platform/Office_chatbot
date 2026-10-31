import { Link } from "react-router";
import { can, useMe } from "../shared/api/session";
import { OwnerImg } from "../shared/ui/Owner";
import { Icon, type IconName } from "../shared/ui/Icon";

/** The soft light the glass shows through, behind a page. */
function Glow() {
  return (
    <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
      <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
      <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
    </div>
  );
}

/** A page's glass header: the icon tile and the title. */
function Intro({ icon, title }: { icon: IconName; title: string }) {
  return (
    <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
      <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
      <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
      <div className="relative flex items-center gap-4">
        <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
          <Icon name={icon} className="size-7" />
        </span>
        <h1 className="text-[26px] leading-tight font-bold">{title}</h1>
      </div>
    </header>
  );
}

/** A side-bar page whose feature is not built yet: says what it will hold and when, not a dead end. */
export function ComingSoon({ title, icon, what, when }: { title: string; icon: IconName; what: string; when: string }) {
  return (
    <div className="relative isolate grid gap-5 pb-4">
      <Glow />
      <Intro icon={icon} title={title} />
      <section className="liquid relative grid justify-items-center gap-3 overflow-hidden rounded-[26px] px-6 py-14 text-center">
        <OwnerImg name="sidebar_art" className="fade-all pointer-events-none absolute bottom-0 left-0 w-44 opacity-20 sm:w-56" />
        <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
        <span className="relative grid size-16 place-items-center rounded-[20px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_10px_24px_-12px_rgb(140_98_58/0.75)]">
          <Icon name={icon} className="size-8" />
        </span>
        <p className="relative text-[20px] font-bold">قريبًا</p>
        <p className="relative max-w-md text-[14px] text-ink-soft">{what}</p>
        <p className="relative inline-flex items-center gap-1.5 rounded-full border border-gold-line bg-gold-tint px-3.5 py-1 text-xs font-semibold text-gold-ink">
          <Icon name="clock" className="size-3.5" />
          {when}
        </p>
        <Link to="/" className="relative mt-2 inline-flex min-h-10 items-center gap-1.5 rounded-full border border-line/60 bg-card/60 px-4 text-sm font-semibold text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint">
          <Icon name="home" className="size-4" />
          ارجع للرئيسية
        </Link>
      </section>
    </div>
  );
}

type Row = { to: string; icon: IconName; title: string; text: string; show: boolean };

/**
 * «الإعدادات» as «liquid glass»: a glass header, then the settings in two groups (your own account,
 * and the office's: shown only to who may change them), each a glass card with what it changes.
 */
export function SettingsPage() {
  const { data: me } = useMe();
  const mine: Row[] = [
    { to: "/account", icon: "account", title: "حسابي", text: "اسمك وإيميلك ودورك في المكتب.", show: true },
    { to: "/change-password", icon: "key", title: "تغيير كلمة السر", text: "كلمة سر جديدة، وكل الأجهزة التانية بتخرج.", show: true },
  ];
  const office: Row[] = [
    { to: "/users", icon: "users", title: "الحسابات", text: "زوّد موظف، اقفل حساب، أو اعمل كلمة سر مؤقتة.", show: can(me, "users.manage") },
    { to: "/lists", icon: "clients", title: "قوايم المكتب", text: "الموكلين الحاليين، وقايمة المستشار، والممنوعين" + (can(me, "conversations.unmute") ? "، والمحادثات الساكتة." : "."), show: can(me, "lists.manage") },
    { to: "/texts", icon: "chat", title: "نصوص حازم", text: "الكلام اللي حازم بيرد بيه، وكل نسخة قديمة محفوظة.", show: can(me, "texts.manage") },
    { to: "/prices", icon: "receipt", title: "الأسعار", text: "الأسعار اللي حازم بيقولها وبيطلبها؛ التعديل للأستاذ والمدير.", show: true },
    { to: "/testing", icon: "refresh", title: "أدوات التجربة", text: "حازم ينسى رقم من أرقام التجربة، علشان تجرّب المحادثة من الأول.", show: can(me, "testing.forget") },
  ];
  return (
    <div className="relative isolate grid gap-5 pb-4">
      <Glow />
      <Intro icon="settings" title="الإعدادات" />
      <Group id="settings-mine" icon="account" title="حسابك" rows={mine} />
      <Group id="settings-office" icon="office" title="المكتب" rows={office} />
    </div>
  );
}

function Group({ id, icon, title, rows }: { id: string; icon: IconName; title: string; rows: Row[] }) {
  const shown = rows.filter((r) => r.show);
  if (shown.length === 0) return null;
  return (
    <section className="grid gap-3" aria-labelledby={id}>
      <div className="flex items-center gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
          <Icon name={icon} className="size-5" />
        </span>
        <h2 id={id} className="text-[17px] font-bold">{title}</h2>
      </div>
      <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {shown.map((r) => (
          <li key={r.to}>
            <Link to={r.to}
              className="group relative flex h-full items-start gap-3 overflow-hidden rounded-[20px] border border-line/60 bg-card/60 p-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)] backdrop-blur-md transition hover:-translate-y-px hover:border-gold-line hover:bg-card/85 hover:shadow-[0_14px_30px_-14px_rgb(60_44_20/0.4)] motion-reduce:transition-none motion-reduce:hover:translate-y-0">
              <span aria-hidden className="absolute inset-y-4 start-0 w-1 rounded-e-full bg-gold opacity-0 transition group-hover:opacity-100" />
              <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
                <Icon name={r.icon} className="size-5" />
              </span>
              <span className="grid min-w-0 flex-1 gap-0.5">
                <span className="font-bold">{r.title}</span>
                <span className="text-sm text-ink-soft">{r.text}</span>
              </span>
              <span aria-hidden className="grid size-8 shrink-0 place-items-center self-center rounded-full border border-line/60 bg-card/70 text-ink-soft transition group-hover:border-gold-line group-hover:bg-gold-tint group-hover:text-gold-ink">
                <Icon name="chevron-start" className="size-4 transition group-hover:-translate-x-0.5" />
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
