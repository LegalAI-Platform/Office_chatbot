import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link, NavLink, Outlet, useNavigate } from "react-router";
import { useAlerts, useMarkAlertsRead } from "../shared/api/alerts";
import { useDashboard } from "../shared/api/dashboard";
import { can, useLogout, useMe } from "../shared/api/session";
import { ALERT_KINDS, label, ROLES, SYSTEM_WHAT, TASK_TYPES } from "../shared/lib/labels";
import { setTheme, useTheme } from "../shared/lib/theme";
import { cairoLongDate, cairoTime, timeAgo } from "../shared/lib/time";
import { Icon } from "../shared/ui/Icon";
import { ConfirmHost, Toaster } from "../shared/ui/feedback";
import { CommandPalette } from "./CommandPalette";
import { prefetch, prefetchAll } from "./lazyPages";
import { OwnerImg, type OwnerImage } from "../shared/ui/Owner";

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

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[16.5rem_minmax(0,1fr)]">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-2 focus:rounded-full focus:bg-side focus:px-4 focus:py-2 focus:text-side-ink">
        روح للمحتوى
      </a>

      {/* computer: the black side bar */}
      <aside className="sticky top-0 hidden h-dvh flex-col overflow-hidden bg-side text-side-ink lg:flex">
        <div className="relative z-10 grid justify-items-center gap-1 px-3 pt-5 pb-4 text-center">
          <OwnerImg name="logo_emblem" className="size-[5.5rem]" />
          <p className="mt-1 text-[16px] leading-tight font-bold text-white">مكتب سعد فتحي سعد</p>
          <p className="text-[12px] text-side-ink/80">للمحاماة والاستشارات القانونية</p>
        </div>
        <nav aria-label="التنقل" className="relative z-10 flex min-h-0 flex-1 flex-col px-2.5 pb-3">
          <div className="grid min-h-0 content-start gap-1 overflow-y-auto">
            <SideLink item={home} />
            {shownGroups.map((g) => (
              <section key={g.title} aria-label={g.title} className="mt-2.5 grid gap-1">
                <h2 className="px-4 pb-0.5 text-[12px] font-semibold text-side-ink/60">{g.title}</h2>
                {g.items.map((i) => <SideLink key={i.to} item={i} />)}
              </section>
            ))}
          </div>
          <div className="mt-auto border-t border-white/10 pt-2.5">
            <SideLink item={settings} />
          </div>
        </nav>
        {/* the scales and the law books, fading into the bar, behind the links */}
        <div className="pointer-events-none absolute inset-x-0 bottom-0 h-[45%]" aria-hidden>
          <OwnerImg name="sidebar_art" className="fade-top absolute bottom-0 left-0 w-full max-w-none" />
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
  return (
    <NavLink to={i.to} end={i.end} onMouseEnter={() => prefetch(i.to)} onFocus={() => prefetch(i.to)}
      className={({ isActive }) => `flex min-h-[2.75rem] items-center gap-3.5 whitespace-nowrap rounded-[10px] px-4 text-[15px] transition ${isActive ? "nav-active font-bold" : "text-side-ink hover:bg-white/5"}`}>
      <NavIcon icon={i.icon} />
      <span className="flex-1">{i.text}</span>
      {i.badge ? <span className="grid size-6 place-items-center rounded-full bg-red text-[12px] font-bold text-white">{i.badge}</span> : null}
      {i.dot ? <span className="size-2 rounded-full bg-red" aria-label="فيه مهام متأخرة" role="img" /> : null}
    </NavLink>
  );
}

/** The owner's white line icons; «الرئيسية» is the filled cream house of the mockup. */
function NavIcon({ icon, small = false }: { icon: Item["icon"]; small?: boolean }) {
  const size = small ? "size-5" : "size-[26px]";
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
    <header className="sticky top-0 z-20 border-b border-line bg-top" style={{ paddingTop: "env(safe-area-inset-top, 0px)" }}>
      <div className="flex min-h-[4.6rem] items-center gap-3 px-4 lg:gap-6 lg:px-7">
        <Link to="/" className="lg:hidden" aria-label="الرئيسية">
          <OwnerImg name="logo_emblem" className="size-11" />
        </Link>
        <Clock />
        <span className="hidden h-9 w-px bg-line md:block" aria-hidden />
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

  return (
    <div ref={box} className="relative shrink-0">
      <button type="button" aria-expanded={open} aria-haspopup="dialog" onClick={() => setOpen((v) => !v)}
        className="relative grid size-11 place-items-center rounded-full text-ink hover:bg-muted"
        aria-label={unread > 0 ? `${unread} تنبيهات جديدة` : late > 0 ? `${late} مهام متأخرة` : "التنبيهات"}>
        <Icon name="bell" className="size-7" />
        {unread > 0 ? (
          <span className="absolute top-0 right-0 grid size-[1.35rem] place-items-center rounded-full bg-red text-[12px] font-bold text-white ring-2 ring-top">{unread > 9 ? "9+" : unread}</span>
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
  const theme = useTheme();
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
  const Row = ({ onClick, children, danger = false }: { onClick: () => void; children: ReactNode; danger?: boolean }) => (
    <button role="menuitem" type="button" onClick={onClick}
      className={`flex min-h-10 w-full items-center gap-2.5 rounded-lg px-3 text-start text-sm hover:bg-muted ${danger ? "text-red-ink" : ""}`}>
      {children}
    </button>
  );
  return (
    <div ref={box} className="relative shrink-0">
      <button type="button" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-3 rounded-[10px] p-1 hover:bg-muted">
        <OwnerImg name="user_profile" className="size-12 rounded-full object-cover" />
        <span className="hidden min-w-0 text-start leading-tight sm:grid">
          <span className="truncate text-[15px] font-bold">{me.display_name}</span>
          <span className="truncate text-xs text-ink-soft">{label(ROLES, me.role)}</span>
        </span>
        <Icon name="chevron-down" className="hidden size-5 text-ink-soft sm:block" />
      </button>
      {open ? (
        <div role="menu" className="surface absolute top-[calc(100%+0.5rem)] left-0 z-30 grid w-56 gap-0.5 rounded-xl p-1.5">
          <Row onClick={() => go("/account")}><Icon name="account" className="size-4" />حسابي</Row>
          <Row onClick={() => go("/change-password")}><Icon name="key" className="size-4" />تغيير كلمة السر</Row>
          {can(me, "users.manage") ? <Row onClick={() => go("/users")}><Icon name="users" className="size-4" />الحسابات</Row> : null}
          <Row onClick={() => setTheme(theme === "dark" ? "light" : "dark")}>
            <Icon name={theme === "dark" ? "sun" : "moon"} className="size-4" />
            {theme === "dark" ? "الوضع الفاتح" : "الوضع الغامق"}
          </Row>
          <hr className="my-1 border-line" />
          <Row danger onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
            <Icon name="logout" className="size-4" />تسجيل الخروج
          </Row>
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
    <div className="hidden shrink-0 items-center gap-3 md:flex">
      <OwnerImg name="top_calendar" className="size-9 object-contain" />
      <span className="grid leading-tight">
        <span className="text-[15px] font-bold">{cairoLongDate(now)}</span>
        <span className="tabular text-[13px] text-ink-soft">القاهرة • {cairoTime(now)}</span>
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
      className="flex min-h-[3.1rem] min-w-0 flex-1 items-center gap-3 rounded-[12px] border border-line-strong bg-card px-4 focus-within:border-gold lg:max-w-[38rem]">
      <OwnerImg name="top_search" className="size-6 shrink-0 object-contain" />
      <input ref={input} type="search" aria-label="بحث" placeholder="ابحث عن عميل، ميعاد، مهمة، أو رقم قضية..."
        className="min-h-10 min-w-0 flex-1 bg-transparent text-[15px] outline-none placeholder:text-ink-soft" />
      <button type="button" onClick={() => setPalette(true)} aria-label="اختصارات وبحث (Ctrl K)"
        className="hidden shrink-0 rounded-md border border-line bg-card-2 px-2.5 py-0.5 font-sans text-sm text-ink-soft hover:border-gold-line sm:block" dir="ltr">Ctrl K</button>
      <CommandPalette open={palette} onClose={() => setPalette(false)} />
    </form>
  );
}
