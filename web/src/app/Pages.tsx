import { Link } from "react-router";
import { can, useMe } from "../shared/api/session";
import { Icon, type IconName } from "../shared/ui/Icon";
import { PageHeader } from "../shared/ui/ui";

/** A side-bar page whose feature is not built yet: says what it will hold and when, not a dead end. */
export function ComingSoon({ title, icon, what }: { title: string; icon: IconName; what: string }) {
  return (
    <div className="grid gap-5">
      <PageHeader title={title} />
      <section className="surface grid justify-items-center gap-2 rounded-[var(--radius-card)] px-6 py-14 text-center">
        <span className="mb-1 grid size-12 place-items-center rounded-full bg-accent-tint text-accent-ink">
          <Icon name={icon} className="size-6" />
        </span>
        <p className="text-[17px] font-semibold">الصفحة دي لسه بتتعمل</p>
        <p className="max-w-md text-[14px] text-ink-soft">{what}</p>
        <Link to="/" className="mt-2 text-[14px] font-medium text-accent-ink hover:underline">ارجع للرئيسية</Link>
      </section>
    </div>
  );
}

export function SettingsPage() {
  const { data: me } = useMe();
  const rows: { to: string; icon: IconName; title: string; text: string; show: boolean }[] = [
    { to: "/account", icon: "account", title: "حسابي", text: "اسمك وإيميلك ودورك في المكتب.", show: true },
    { to: "/change-password", icon: "key", title: "تغيير كلمة السر", text: "كلمة سر جديدة، وكل الأجهزة التانية بتخرج.", show: true },
    { to: "/users", icon: "users", title: "الحسابات", text: "زوّد موظف، اقفل حساب، أو اعمل كلمة سر مؤقتة.", show: can(me, "users.manage") },
    { to: "/lists", icon: "clients", title: "قوايم المكتب", text: "الموكلين الحاليين، وقايمة الأستاذ، والممنوعين" + (can(me, "conversations.unmute") ? "، والمحادثات الساكتة." : "."), show: can(me, "lists.manage") },
    { to: "/texts", icon: "chat", title: "نصوص حازم", text: "الكلام اللي حازم بيرد بيه، وكل نسخة قديمة محفوظة.", show: can(me, "texts.manage") },
  ];
  return (
    <div className="grid gap-5">
      <PageHeader section="/settings" title="الإعدادات" description="حسابك، والحسابات، والقوايم، والنصوص اللي حازم بيرد بيها." />
      <ul className="surface grid divide-y divide-line overflow-hidden rounded-[var(--radius-card)]">
        {rows.filter((r) => r.show).map((r) => (
          <li key={r.to}>
            <Link to={r.to} className="flex items-center gap-4 px-4 py-3.5 transition-colors hover:bg-card-2 lg:px-5">
              <span className="grid size-10 shrink-0 place-items-center rounded-full bg-muted text-ink-soft"><Icon name={r.icon} className="size-5" /></span>
              <span className="grid min-w-0 flex-1 leading-snug">
                <span className="font-medium">{r.title}</span>
                <span className="text-[13.5px] text-ink-soft">{r.text}</span>
              </span>
              <Icon name="chevron-end" className="size-4 shrink-0 text-ink-faint" />
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
