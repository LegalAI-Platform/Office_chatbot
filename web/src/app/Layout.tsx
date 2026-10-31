import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link, NavLink, Outlet, useNavigate } from "react-router";
import { useAlerts, useMarkAlertsRead } from "../shared/api/alerts";
import { useDashboard } from "../shared/api/dashboard";
import { avatarUrl, can, useLogout, useMe } from "../shared/api/session";
import { ALERT_KINDS, label, ROLES, SYSTEM_WHAT, TASK_TYPES } from "../shared/lib/labels";
import { mayAskToNotify, useAlertNotifications } from "../shared/lib/notify";
import { cairoLongDate, cairoTime, timeAgo } from "../shared/lib/time";
import { Avatar } from "../shared/ui/Avatar";
import { Icon, type IconName } from "../shared/ui/Icon";
import { ConfirmHost, Toaster } from "../shared/ui/feedback";
import { CommandPalette } from "./CommandPalette";
import { prefetchAll, usePrefetch } from "./lazyPages";
import { OwnerImg, type OwnerImage } from "../shared/ui/Owner";
import { ThemePicker } from "../shared/ui/ThemePicker";

type Item = { to: string; text: string; short: string; icon: OwnerImage | "home" | "chat" | "receipt" | "document"; dot?: boolean; badge?: number; end?: boolean };

/**
 * The shell of the owner's mockups («الرئيسية», light and dark): the black side bar with the seal,
 * the sections under their headings, «الإعدادات» at the bottom and the scales behind; the top bar
 * with the Cairo date, the bell, the search and the person. On a phone: a top bar and a tab bar.
 */
export function Layout() {
  const { data: dash } = useDashboard();
  const late = dash?.overdue ?? 0;

  const home: Item = { to: "/", text: "الرئيسية", short: "الرئيسية", icon: "home", end: true };
  const settings: Item = { to: "/settings", text: "الإعدادات", short: "الإعدادات", icon: "nav_settings" };
  // The side bar in sections, like the owner's second mockup; «الإعدادات» sits alone at the bottom.
  const groups: { title: string; items: Item[] }[] = [
    {
      title: "الشغل اليومي",
      items: [
        { to: "/tasks", text: "الطلبات / المهام", short: "المهام", icon: "nav_tasks", dot: late > 0 },
        { to: "/appointments", text: "المواعيد", short: "المواعيد", icon: "nav_appointments" },
        { to: "/chats", text: "المحادثات", short: "المحادثات", icon: "chat" },
        { to: "/clients", text: "الشركات / العملاء", short: "العملاء", icon: "nav_clients" },
      ],
    },
    {
      title: "الفلوس والخدمات",
      items: [
        { to: "/receipts", text: "الإيصالات والمدفوعات", short: "الإيصالات", icon: "receipt" },
        { to: "/services", text: "طلبات الخدمات", short: "الخدمات", icon: "document" },
        { to: "/urf", text: "حالات العرفي", short: "العرفي", icon: "nav_documents" },
        { to: "/documents", text: "المستندات", short: "المستندات", icon: "nav_documents" },
      ],
    },
    { title: "الإدارة", items: [{ to: "/reports", text: "التقارير", short: "التقارير", icon: "nav_reports" }] },
  ];
  // TASK 07c: the reports are for the manager and the lawyer; the others never see the entry
  // (nor the «الإدارة» heading, left empty without it).
  const { data: me } = useMe();
  const shownGroups = groups
    .map((g) => ({ ...g, items: g.items.filter((i) => i.to !== "/reports" || can(me, "reports.view")) }))
    .filter((g) => g.items.length > 0);
  const items = [home, ...groups.flatMap((g) => g.items), settings];
  // Speed (idea from PR #14): every page's file once the browser is idle, so a click never waits.
  useEffect(() => { prefetchAll(items.map((i) => i.to)); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const phoneTabs = items.filter((i) => ["/", "/tasks", "/appointments", "/chats", "/settings"].includes(i.to));
  const prefetch = usePrefetch();

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[16.5rem_minmax(0,1fr)]">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-2 focus:rounded-full focus:bg-side focus:px-4 focus:py-2 focus:text-side-ink">
        روح للمحتوى
      </a>

      {/* computer: the black side bar */}
      {/* computer: the black side bar, as «liquid glass» (the owner: modern, clearly worked on): the links are glass
          pills with their icons in small glass tiles, over the scales behind */}
      <aside className="sticky top-0 hidden h-dvh flex-col overflow-hidden bg-side text-side-ink lg:flex">
        {/* a thin gold line along the bar's inner edge */}
        <span aria-hidden className="pointer-events-none absolute inset-y-0 end-0 z-20 w-px bg-gradient-to-b from-transparent via-gold/45 to-transparent" />
        {/* the seal and the name on plain black (the owner kept this part as it was) */}
        <div className="relative z-10 grid justify-items-center gap-1 px-3 pt-5 pb-4 text-center">
          <OwnerImg name="logo_emblem" className="size-[5.5rem]" />
          <p className="mt-1 text-[16px] leading-tight font-bold text-white">مكتب سعد فتحي سعد</p>
          <p className="text-[12px] text-side-ink/80">للمحاماة والاستشارات القانونية</p>
        </div>
        <nav aria-label="التنقل" className="relative z-10 flex min-h-0 flex-1 flex-col px-2.5 pb-3">
          <div className="soft-scroll grid min-h-0 grid-cols-[minmax(0,1fr)] content-start gap-1 overflow-x-hidden overflow-y-auto px-0.5">
            <SideLink item={home} />
            {shownGroups.map((g) => (
              <section key={g.title} aria-label={g.title} className="mt-2.5 grid grid-cols-[minmax(0,1fr)] gap-1">
                <h2 className="flex items-center gap-2 px-3 pb-0.5 text-[11.5px] font-semibold tracking-wide text-gold/85 [text-shadow:0_1px_3px_rgb(0_0_0/0.8)]">
                  {g.title}
                  <span aria-hidden className="h-px flex-1 bg-gradient-to-l from-gold/40 to-transparent" />
                </h2>
                {g.items.map((i) => <SideLink key={i.to} item={i} />)}
              </section>
            ))}
          </div>
          <div className="mt-auto pt-2.5">
            <div className="rounded-[16px] border border-white/10 bg-white/[0.04] p-1 shadow-[inset_0_1px_0_rgb(255_255_255/0.08)] backdrop-blur-md">
              <SideLink item={settings} />
            </div>
          </div>
        </nav>
        {/* the scales and the law books, fading into the bar, behind the links; darker under the words so they read */}
        <div className="pointer-events-none absolute inset-x-0 bottom-0 h-[45%]" aria-hidden>
          <OwnerImg name="sidebar_art" className="fade-top absolute bottom-0 left-0 w-full max-w-none" />
          <span className="absolute inset-0 bg-gradient-to-l from-side/75 via-side/30 to-transparent" />
        </div>
      </aside>

      <div className="flex min-h-dvh min-w-0 flex-col">
        <TopBar late={late} />

        <main id="main" className="w-full flex-1 px-4 pt-4 pb-28 lg:px-7 lg:pt-5 lg:pb-8">
          <Outlet />
        </main>
        <Toaster />
        <ConfirmHost />

        {/* phone: the tab bar */}
        <nav aria-label="التنقل" className="fixed inset-x-0 bottom-0 z-20 grid grid-cols-5 border-t border-side-line bg-side px-1 pt-1 text-side-ink lg:hidden"
          style={{ paddingBottom: "calc(env(safe-area-inset-bottom, 0px) + 0.25rem)" }}>
          {phoneTabs.map((i) => (
            <NavLink key={i.to} to={i.to} end={i.end} onFocus={() => prefetch(i.to)} onTouchStart={() => prefetch(i.to)}
              className={({ isActive }) => `relative grid min-h-14 content-center justify-items-center gap-0.5 rounded-lg text-[11px] ${isActive ? "text-gold" : "text-side-soft"}`}>
              <NavIcon icon={i.icon} small />
              {i.short}
              {i.dot ? <span className="absolute top-2 left-[calc(50%-14px)] size-2 rounded-full bg-red" aria-hidden /> : null}
            </NavLink>
          ))}
        </nav>
      </div>
    </div>
  );
}

function SideLink({ item: i }: { item: Item }) {
  const prefetch = usePrefetch();
  return (
    <NavLink to={i.to} end={i.end} onMouseEnter={() => prefetch(i.to)} onFocus={() => prefetch(i.to)}
      className={({ isActive }) => `group relative flex min-h-[2.85rem] items-center gap-2.5 whitespace-nowrap rounded-[14px] border ps-1.5 pe-2.5 text-[14.5px] transition ${isActive
        ? "nav-active border-[#d6b088]/40 font-bold"
        : "border-transparent text-side-ink hover:border-white/10 hover:bg-white/[0.06] hover:shadow-[inset_0_1px_0_rgb(255_255_255/0.08)] hover:backdrop-blur-md"}`}>
      {({ isActive }) => (
        <>
          {/* the icon in its own small glass tile; gold-lit on the open page */}
          <span className={`grid size-8 shrink-0 place-items-center rounded-[10px] border transition ${isActive
            ? "border-white/30 bg-white/15 shadow-[inset_0_1px_0_rgb(255_255_255/0.35)]"
            : "border-white/10 bg-white/[0.05] shadow-[inset_0_1px_0_rgb(255_255_255/0.06)] group-hover:border-gold/40 group-hover:bg-gold/10"}`}>
            <NavIcon icon={i.icon} />
          </span>
          <span className="min-w-0 flex-1 truncate [text-shadow:0_1px_3px_rgb(0_0_0/0.7)]">{i.text}</span>
          {i.badge ? <span className="grid size-6 place-items-center rounded-full bg-red text-[12px] font-bold text-white">{i.badge}</span> : null}
          {i.dot ? <span className="size-2 rounded-full bg-red shadow-[0_0_0_3px_rgb(239_68_68/0.2),0_0_10px_rgb(239_68_68/0.8)]" aria-label="فيه مهام متأخرة" role="img" /> : null}
          {isActive ? null : <Icon name="chevron-start" className="size-3.5 shrink-0 text-side-soft opacity-0 transition group-hover:opacity-100" />}
        </>
      )}
    </NavLink>
  );
}

/** The owner's white line icons; «الرئيسية» is the filled cream house of the mockup. */
function NavIcon({ icon }: { icon: Item["icon"]; small?: boolean }) {
  const size = "size-5";
  if (icon === "home") {
    return (
      <svg viewBox="0 0 24 24" className={`${size} shrink-0`} aria-hidden>
        <path d="M12 3.2 2.6 11a.9.9 0 0 0 .6 1.6H5v7.4c0 .6.4 1 1 1h4v-5.5h4V21h4c.6 0 1-.4 1-1v-7.4h1.8a.9.9 0 0 0 .6-1.6z" fill="#f8e4bf" />
      </svg>
    );
  }
  // the cut has no chat or receipt icon: the same lines drawn here, in the same white
  if (icon === "chat" || icon === "receipt" || icon === "document") return <Icon name={icon} className={`${size} shrink-0 text-white`} />;
  // these PNGs are white strokes made for the black bar, the same in both cuts
  return <OwnerImg name={icon} className={`${size} shrink-0 object-contain`} />;
}

function TopBar({ late }: { late: number }) {
  return (
    // glass: the page scrolls under a blurred, see-through bar with a thin gold line at its foot
    <header className="sticky top-0 z-20 border-b border-line/60 bg-[color-mix(in_oklab,var(--top)_78%,transparent)] backdrop-blur-xl backdrop-saturate-150"
      style={{ paddingTop: "env(safe-area-inset-top, 0px)" }}>
      <span aria-hidden className="pointer-events-none absolute inset-x-0 bottom-0 h-px bg-gradient-to-l from-transparent via-gold/50 to-transparent" />
      <div className="flex min-h-[4.6rem] items-center gap-3 px-4 lg:gap-4 lg:px-7">
        <Link to="/" className="lg:hidden" aria-label="الرئيسية">
          <OwnerImg name="logo_emblem" className="size-11" />
        </Link>
        <Clock />
        <Bell late={late} />
        <Search />
        <UserMenu />
      </div>
    </header>
  );
}

/** TASK 07c: the office's alerts (overdue, urgent, a client asking for someone) that this person may see. */
function Bell({ late }: { late: number }) {
  const alerts = useAlerts();
  const read = useMarkAlertsRead();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent | KeyboardEvent) => {
      if (e instanceof KeyboardEvent ? e.key === "Escape" : !box.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", close);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", close);
    };
  }, [open]);
  const unread = alerts.data?.unread ?? 0;
  const items = alerts.data?.items ?? [];
  const now = new Date();
  const openTask = useCallback((id: string) => void navigate(`/tasks/${id}`), [navigate]);
  useAlertNotifications(alerts.data?.items, unread, openTask);
  const [ask, setAsk] = useState(mayAskToNotify);

  return (
    <div ref={box} className="relative shrink-0">
      <button type="button" aria-expanded={open} aria-haspopup="dialog" onClick={() => setOpen((v) => !v)}
        className={`relative grid size-11 place-items-center rounded-full border text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_2px_8px_rgb(0_0_0/0.04)] transition duration-200
          ${open ? "border-gold-line bg-card text-gold-ink" : "border-line/80 bg-card/70 hover:border-gold-line hover:bg-card hover:text-gold-ink"}`}
        aria-label={unread > 0 ? `${unread} تنبيهات جديدة` : late > 0 ? `${late} مهام متأخرة` : "التنبيهات"}>
        <Icon name="bell" className="size-[22px]" />
        {unread > 0 ? (
          <>
            <span aria-hidden className="absolute -top-0.5 -right-0.5 size-[1.3rem] animate-ping rounded-full bg-red/40 motion-reduce:hidden" />
            <span className="absolute -top-0.5 -right-0.5 grid size-[1.3rem] place-items-center rounded-full bg-gradient-to-b from-[#f05252] to-[#c81e1e] text-[11px] font-bold text-white shadow-sm ring-2 ring-card">{unread > 9 ? "9+" : unread}</span>
          </>
        ) : null}
      </button>
      {open ? (
        <div role="dialog" aria-label="التنبيهات" className="surface absolute top-[calc(100%+0.5rem)] left-0 z-30 grid w-[min(22rem,calc(100vw-2rem))] gap-1 rounded-xl p-2">
          <div className="flex items-center justify-between px-2 py-1">
            <span className="font-bold">التنبيهات</span>
            {unread > 0 ? (
              <button type="button" className="text-xs font-semibold text-gold-ink hover:underline" onClick={() => read.mutate(undefined)}>
                علّم الكل مقروء
              </button>
            ) : null}
          </div>
          {ask ? (
            <button type="button" onClick={() => void Notification.requestPermission().then(() => setAsk(mayAskToNotify()))}
              className="mx-1 flex items-center gap-2 rounded-lg border border-gold-line bg-gold-tint px-2.5 py-2 text-start text-xs font-semibold text-gold-ink hover:brightness-105">
              <Icon name="bell" className="size-4 shrink-0" />
              شغّل إشعارات الطلبات العاجلة وإنت في تاب تاني
            </button>
          ) : null}
          {items.length === 0 ? (
            <p className="px-2 py-6 text-center text-sm text-ink-soft">مفيش تنبيهات.</p>
          ) : (
            <ul className="grid max-h-[60vh] gap-0.5 overflow-y-auto">
              {items.map((a) => (
                <li key={a.id}>
                  <button type="button" onClick={() => { if (!a.read) read.mutate([a.id]); setOpen(false); if (a.task_id) navigate(`/tasks/${a.task_id}`); }}
                    className={`grid w-full gap-0.5 rounded-lg px-2.5 py-2 text-start hover:bg-muted ${a.read ? "opacity-70" : ""}`}>
                    <span className="flex items-center gap-2 text-sm font-bold">
                      {!a.read ? <span className="size-2 shrink-0 rounded-full bg-red" aria-label="جديد" role="img" /> : null}
                      {label(ALERT_KINDS, a.kind)}
                      <span className="ms-auto text-[11px] font-normal text-ink-soft">{timeAgo(a.created_at, now)}</span>
                    </span>
                    <span className="truncate text-xs text-ink-soft">
                      {a.kind === "system" ? label(SYSTEM_WHAT, a.system_what ?? "") : `${label(TASK_TYPES, a.task_type ?? "")} · ${a.client_name ?? "رقم من غير اسم"}`}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </div>
  );
}

function UserMenu() {
  const { data: me } = useMe();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const logout = useLogout();
  const navigate = useNavigate();
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent | KeyboardEvent) => {
      if (e instanceof KeyboardEvent ? e.key === "Escape" : !box.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", close);
    document.addEventListener("keydown", close);
    return () => {
      document.removeEventListener("mousedown", close);
      document.removeEventListener("keydown", close);
    };
  }, [open]);
  if (!me) return null;
  const go = (to: string) => {
    setOpen(false);
    navigate(to);
  };
  // one row of the menu: its icon in a small glass tile, the words, and an arrow that moves in on hover
  const Row = ({ onClick, icon, children, hint, danger = false }: {
    onClick: () => void; icon: IconName; children: ReactNode; hint?: string; danger?: boolean;
  }) => (
    <button role="menuitem" type="button" onClick={onClick}
      className={`group/row flex min-h-11 w-full items-center gap-3 rounded-[14px] px-2.5 text-start text-[13.5px] transition-colors duration-150
        ${danger ? "text-red-ink hover:bg-red-bg/70" : "hover:bg-card/80"}`}>
      <span className={`grid size-8 shrink-0 place-items-center rounded-[10px] border transition-colors duration-150
        ${danger ? "border-red/20 bg-red-bg text-red-ink" : "border-line/70 bg-card/70 text-ink-soft group-hover/row:border-gold-line group-hover/row:text-gold-ink"}`}>
        <Icon name={icon} className="size-4" />
      </span>
      <span className="grid min-w-0 flex-1 leading-tight">
        <span className="truncate font-semibold">{children}</span>
        {hint ? <span className="truncate text-[11.5px] font-normal text-ink-soft">{hint}</span> : null}
      </span>
      {danger ? null : (
        <Icon name="chevron-start" className="size-4 shrink-0 text-ink-soft opacity-0 transition duration-150 group-hover/row:opacity-100 group-hover/row:-translate-x-0.5" />
      )}
    </button>
  );
  return (
    // ms-auto: toward the far end of the bar (the left, in Arabic), with some room before the edge
    <div ref={box} className="relative ms-auto shrink-0 lg:me-24 2xl:me-40">
      <button type="button" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((v) => !v)}
        className={`group flex items-center gap-2.5 rounded-full border py-1 ps-1 pe-1 shadow-[0_1px_2px_rgb(0_0_0/0.04)] transition duration-200 sm:pe-3
          focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-gold
          ${open ? "border-gold-line bg-card shadow-[0_6px_18px_rgb(0_0_0/0.08)]" : "border-line bg-card hover:border-gold-line hover:shadow-[0_4px_14px_rgb(0_0_0/0.07)]"}`}>
        {/* the photo (or the initials) with a soft gold ring and a small «signed in» dot */}
        <span className="relative shrink-0">
          <Avatar name={me.display_name} photo={avatarUrl(me)} className="size-10 text-sm ring-2 ring-gold-tint ring-offset-1 ring-offset-card" />
          <span className="absolute -bottom-0.5 -start-0.5 size-3 rounded-full bg-green ring-2 ring-card" aria-hidden />
        </span>
        <span className="hidden min-w-0 max-w-[11rem] text-start leading-tight sm:grid">
          <span className="truncate text-[14px] font-bold">{me.display_name}</span>
          <span className="truncate text-[11.5px] font-medium text-gold-ink">{label(ROLES, me.role)}</span>
        </span>
        <Icon name="chevron-down" className={`hidden size-4 text-ink-soft transition-transform duration-200 sm:block ${open ? "rotate-180" : "group-hover:translate-y-0.5"}`} />
      </button>
      {open ? (
        <div role="menu" className="liquid menu-glass menu-pop absolute top-[calc(100%+0.6rem)] left-0 z-30 grid w-[min(18.5rem,calc(100vw-2rem))] gap-1 overflow-hidden rounded-[22px] p-2">
          {/* who is signed in: the photo, the name, the e-mail and the role, over a soft gold light */}
          <div className="relative mb-1 flex items-center gap-3 overflow-hidden rounded-[16px] border border-gold-line/50 bg-gradient-to-br from-gold-tint via-card/60 to-card/30 p-3">
            <span aria-hidden className="absolute -top-10 -start-10 size-28 rounded-full bg-gold/25 blur-2xl" />
            <Avatar name={me.display_name} photo={avatarUrl(me)} className="relative size-12 text-base ring-2 ring-card" />
            <span className="relative grid min-w-0 flex-1 leading-tight">
              <span className="truncate text-[14.5px] font-bold">{me.display_name}</span>
              <span className="truncate text-[11.5px] text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>{me.email}</span>
              <span className="mt-1 w-fit rounded-full bg-card/80 px-2 py-0.5 text-[11px] font-semibold text-gold-ink">{label(ROLES, me.role)}</span>
            </span>
          </div>
          <Row icon="account" hint="بياناتك وصورتك" onClick={() => go("/account")}>حسابي</Row>
          <Row icon="key" hint="غيّرها كل فترة" onClick={() => go("/change-password")}>تغيير كلمة السر</Row>
          {can(me, "users.manage") ? <Row icon="users" hint="حسابات المكتب وصلاحياتها" onClick={() => go("/users")}>الحسابات</Row> : null}
          <div className="mx-1 my-1 h-px bg-gradient-to-l from-transparent via-line to-transparent" aria-hidden />
          <div className="grid gap-2 px-2.5 pt-0.5 pb-1.5">
            <span className="flex items-center gap-1.5 text-[11.5px] font-semibold text-ink-soft" id="look-label">
              <Icon name="sun" className="size-3.5" />الشكل
            </span>
            <ThemePicker labelledBy="look-label" />
          </div>
          <div className="mx-1 my-1 h-px bg-gradient-to-l from-transparent via-line to-transparent" aria-hidden />
          <Row icon="logout" danger onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>تسجيل الخروج</Row>
        </div>
      ) : null}
    </div>
  );
}

/** «السبت 25 أكتوبر 2026» over «القاهرة • 9:15 ص», with the calendar icon. */
function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(t);
  }, []);
  return (
    <div className="hidden shrink-0 items-center gap-2.5 rounded-2xl border border-line/80 bg-card/60 py-1.5 ps-1.5 pe-3.5 shadow-[inset_0_1px_0_rgb(255_255_255/0.4)] md:flex">
      <span className="grid size-9 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="calendar" className="size-5" />
      </span>
      <span className="grid leading-tight">
        <span className="text-[14px] font-bold">{cairoLongDate(now)}</span>
        <span className="tabular flex items-center gap-1.5 text-[12px] text-ink-soft">
          <span className="size-1.5 rounded-full bg-green" aria-hidden />القاهرة • {cairoTime(now)}
        </span>
      </span>
    </div>
  );
}

/** Finds tasks by the client's name or the last digits of the number; Ctrl K opens the quick search (CommandPalette). */
function Search() {
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);
  const [palette, setPalette] = useState(false);
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette(true);
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, []);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const q = input.current?.value.trim() ?? "";
    navigate(q ? `/tasks?q=${encodeURIComponent(q)}` : "/tasks");
  };
  return (
    <form role="search" onSubmit={submit}
      className="group/search flex min-h-[2.9rem] min-w-0 flex-1 items-center gap-2.5 rounded-full border border-line/80 bg-card/70 ps-4 pe-2 shadow-[inset_0_1px_2px_rgb(0_0_0/0.04)] transition duration-200
        hover:border-gold-line focus-within:border-gold focus-within:bg-card focus-within:shadow-[0_0_0_4px_rgb(176_133_82/0.16)] lg:max-w-[36rem]">
      <Icon name="search" className="size-[18px] shrink-0 text-ink-soft transition-colors group-focus-within/search:text-gold-ink" />
      <input ref={input} type="search" aria-label="بحث" placeholder="ابحث عن عميل، ميعاد، مهمة، أو رقم قضية..."
        className="min-h-10 min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-ink-soft/80" />
      <button type="button" onClick={() => setPalette(true)} aria-label="اختصارات وبحث (Ctrl K)"
        className="hidden shrink-0 items-center gap-1 rounded-full px-1.5 py-1 font-sans text-[11px] font-semibold text-ink-soft transition hover:bg-card-2 sm:flex" dir="ltr">
        <kbd className="rounded-md border border-line border-b-2 bg-card px-1.5 py-0.5 font-sans">Ctrl</kbd>
        <kbd className="rounded-md border border-line border-b-2 bg-card px-1.5 py-0.5 font-sans">K</kbd>
      </button>
      <CommandPalette open={palette} onClose={() => setPalette(false)} />
    </form>
  );
}
