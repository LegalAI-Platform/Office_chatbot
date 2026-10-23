import { Link } from "react-router";
import { can, useMe } from "../shared/api/session";
import { OwnerImg } from "../shared/ui/Owner";
import { Icon, type IconName } from "../shared/ui/Icon";

/** A side-bar page whose feature is not built yet: says what it will hold and when, not a dead end. */
export function ComingSoon({ title, icon, what, when }: { title: string; icon: IconName; what: string; when: string }) {
  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">{title}</h1>
      <section className="surface relative grid justify-items-center gap-3 overflow-hidden rounded-[var(--radius-card)] px-6 py-14 text-center">
        <OwnerImg name="sidebar_art" className="pointer-events-none absolute bottom-0 left-0 w-44 opacity-25" />
        <span className="grid size-14 place-items-center rounded-2xl bg-side text-gold">
          <Icon name={icon} className="size-7" />
        </span>
        <p className="text-lg font-bold">قريبًا</p>
        <p className="max-w-md text-sm text-ink-soft">{what}</p>
        <p className="rounded-md bg-gold-tint px-3 py-1 text-xs font-semibold text-gold-ink">{when}</p>
        <Link to="/" className="mt-2 text-sm font-semibold text-gold-ink hover:underline">ارجع للرئيسية</Link>
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
  ];
  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">الإعدادات</h1>
      <ul className="grid gap-3 md:grid-cols-3">
        {rows.filter((r) => r.show).map((r) => (
          <li key={r.to}>
            <Link to={r.to} className="surface flex h-full items-start gap-3 rounded-[var(--radius-card)] p-4 hover:border-gold-line">
              <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-side text-gold"><Icon name={r.icon} className="size-5" /></span>
              <span className="grid gap-0.5">
                <span className="font-bold">{r.title}</span>
                <span className="text-sm text-ink-soft">{r.text}</span>
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
