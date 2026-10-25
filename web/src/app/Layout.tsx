import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { useAlerts, useMarkAlertsRead } from "../shared/api/alerts";
import { api } from "../shared/api/client";
import { useDashboard } from "../shared/api/dashboard";
import { can, useLogout, useMe } from "../shared/api/session";
import { ALERT_KINDS, label, ROLES, TASK_TYPES } from "../shared/lib/labels";
import { setTheme, useTheme } from "../shared/lib/theme";
import { cairoLongDate, cairoTime, timeAgo } from "../shared/lib/time";
import { FirmLogo } from "../shared/ui/Brand";
import { ConfirmHost, Toaster } from "../shared/ui/feedback";
import { CommandPalette } from "./CommandPalette";
import { prefetch, prefetchAll } from "./lazyPages";
import { Icon, type IconName } from "../shared/ui/Icon";

type Item = { to: string; text: string; short: string; icon: IconName; dot?: boolean; end?: boolean; count?: number; hot?: boolean };

/**
 * The shell («Night desk»): on a computer, the navy rail (the firm's emblem, each section as an icon with its
 * short name and the count that needs a person) and a top bar with the search, the date, the bell and the
 * person; on a phone, a top bar and a tab bar.
 */
export function Layout() {
  const { data: dash } = useDashboard();
  const late = dash?.overdue ?? 0;
  const { data: me } = useMe();
  // the receipts waiting for a person, shown on their entry (TASK 09: shared with the home screen's query)
  const receipts = useQuery({ queryKey: ["payments", "review"], enabled: can(me, "schedule.manage"), queryFn: () => api<unknown[]>("GET", "/payments?show=review") });
  // the pages reached from «الإعدادات» keep it lit in the rail and the tab bar
  const { pathname } = useLocation();
  const inSettings = SETTINGS_PAGES.some((p) => pathname.startsWith(p));

  const items: Item[] = [
    { to: "/", text: "الرئيسية", short: "الرئيسية", icon: "home", end: true },
    { to: "/tasks", text: "المهام", short: "المهام", icon: "tasks", dot: late > 0, count: (dash?.urgent ?? 0) + late, hot: true },
    { to: "/appointments", text: "المواعيد", short: "المواعيد", icon: "calendar" },
    { to: "/chats", text: "المحادثات", short: "المحادثات", icon: "chat" },
    { to: "/receipts", text: "الإيصالات والمدفوعات", short: "الإيصالات", icon: "wallet", count: receipts.data?.length ?? 0 },
    { to: "/services", text: "طلبات الخدمات", short: "الخدمات", icon: "file-text" },
    { to: "/urf", text: "حالات العرفي", short: "العرفي", icon: "scale" },
    { to: "/reports", text: "التقارير", short: "التقارير", icon: "reports" },
  ];
  // TASK 07c: the reports are for the manager and the lawyer; the others never see the entry.
  const shownItems = items.filter((i) => i.to !== "/reports" || can(me, "reports.view"));
  const phoneTabs = [...items.filter((i) => ["/", "/tasks", "/appointments", "/chats"].includes(i.to)),
    { to: "/settings", text: "الإعدادات", short: "الإعدادات", icon: "settings" as const }];

  const ahead = usePrefetch();
  // once the panel has settled, the other pages' files too, so moving around never waits for a download
  useEffect(() => prefetchAll([...items.map((i) => i.to), ...SETTINGS_PAGES, "/tasks/:id"]), []);

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[6.25rem_minmax(0,1fr)]">
      <div className="backdrop" aria-hidden />
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:m-2 focus:rounded-md focus:bg-card focus:px-4 focus:py-2 focus:shadow-[var(--shadow-pop)]">
        روح للمحتوى
      </a>

      {/* computer: the rail */}
      <aside className="glass-side sticky top-0 z-10 hidden h-dvh flex-col items-center lg:flex">
        <NavLink to="/" aria-label="الرئيسية" title="مكتب سعد فتحي سعد للمحاماة" className="grid h-[4.75rem] shrink-0 place-items-center">
          <FirmLogo className="size-12" onDark />
        </NavLink>
        <nav aria-label="التنقل" className="grid w-full content-start gap-1 overflow-y-auto px-2 pb-2 [scrollbar-width:none]">
          {GROUPS.map((g, n) => {
            const list = shownItems.filter((i) => g.includes(i.to));
            return list.length ? (
              <div key={n} className={`grid gap-0.5 ${n > 0 ? "border-t border-side-line pt-1.5" : ""}`}>
                {list.map((i) => <RailLink key={i.to} item={i} />)}
              </div>
            ) : null;
          })}
        </nav>
        <div className="mt-auto w-full shrink-0 border-t border-side-line px-2 py-2">
          <RailLink item={{ to: "/settings", text: "الإعدادات", short: "الإعدادات", icon: "settings" }} lit={inSettings} />
        </div>
      </aside>

      <div className="relative z-[1] flex min-h-dvh min-w-0 flex-col">
        <TopBar late={late} />

        <main id="main" className="relative z-[1] mx-auto w-full max-w-[92rem] flex-1 px-4 pt-5 pb-28 lg:px-7 lg:pt-6 lg:pb-10">
          {/* keyed by the page: each page arrives with a short fade instead of a jump */}
          <div key={pathname} className="page-in">
            <Outlet />
          </div>
        </main>
        <Toaster />
        <ConfirmHost />

        {/* phone: the tab bar */}
        <nav aria-label="التنقل" className="glass-bar fixed inset-x-0 bottom-0 z-20 grid grid-cols-5 border-t px-1 pt-1 lg:hidden"
          style={{ paddingBottom: "calc(env(safe-area-inset-bottom, 0px) + 0.25rem)" }}>
          {phoneTabs.map((i) => (
            <NavLink key={i.to} to={i.to} end={i.end} onTouchStart={() => ahead(i.to)}
              className={({ isActive }) => `relative grid min-h-14 content-center justify-items-center gap-0.5 rounded-lg text-[11.5px] ${isActive || (i.to === "/settings" && inSettings) ? "font-semibold text-accent-ink" : "text-ink-soft"}`}>
              <Icon name={i.icon} className="size-[22px]" />
              {i.short}
              {i.dot ? <span className="absolute top-2 left-[calc(50%-14px)] size-2 rounded-full bg-red ring-2 ring-top" aria-hidden /> : null}
            </NavLink>
          ))}
        </nav>
      </div>
    </div>
  );
}

/** A page's file and its first data, fetched when the pointer reaches its link (once per half minute: the cache's staleTime). */
function usePrefetch() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  return (to: string) => prefetch(to, qc, me);
}

const SETTINGS_PAGES = ["/settings", "/lists", "/texts", "/users", "/account", "/change-password"];

/** The rail's groups, split by a fine line: home, the day's work, money and written work, the reports. */
const GROUPS: string[][] = [["/"], ["/tasks", "/appointments", "/chats"], ["/receipts", "/services", "/urf"], ["/reports"]];

/** One section in the rail: its icon, its short name under it, and the count that needs a person. */
function RailLink({ item: i, lit = false }: { item: Item; lit?: boolean }) {
  const ahead = usePrefetch();
  return (
    <NavLink to={i.to} end={i.end} title={i.text} onMouseEnter={() => ahead(i.to)} onFocus={() => ahead(i.to)}
      className={({ isActive }) => `group relative grid justify-items-center gap-1 rounded-[12px] px-1 py-2 text-[11.5px] leading-tight transition-colors ${
        isActive || lit ? "bg-gold/15 font-semibold text-side-ink" : "text-side-soft hover:bg-white/5 hover:text-side-ink"}`}>
      {({ isActive }) => (
        <>
          <Icon name={i.icon} className={`size-[21px] ${isActive || lit ? "text-gold" : ""}`} />
          <span className="max-w-full truncate">{i.short}</span>
          {i.count ? (
            <span className={`tabular absolute top-1 start-[calc(50%+0.4rem)] grid h-[1.1rem] min-w-[1.1rem] place-items-center rounded-full px-1 text-[10.5px] font-bold ring-2 ring-side ${i.hot ? "bg-coral-tile text-white" : "bg-gold text-on-accent"}`}
              aria-label={`${i.count} محتاجين حد`}>{i.count}</span>
          ) : null}
        </>
      )}
    </NavLink>
  );
}

function TopBar({ late }: { late: number }) {
  return (
    <header className="glass-bar sticky top-0 z-20 border-b" style={{ paddingTop: "env(safe-area-inset-top, 0px)" }}>
      <div className="flex h-16 items-center gap-2 px-4 sm:gap-3 lg:px-8">
        <NavLinkHome />
        <Search />
        <span className="flex-1" />
        <Clock />
        <Bell late={late} />
        <UserMenu />
      </div>
    </header>
  );
}

function NavLinkHome() {
  return (
    <NavLink to="/" className="lg:hidden" aria-label="الرئيسية">
      <FirmLogo className="size-10" />
    </NavLink>
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
        className="relative grid size-10 place-items-center rounded-full text-ink-soft transition-colors hover:bg-muted hover:text-ink"
        aria-label={unread > 0 ? `${unread} تنبيهات جديدة` : late > 0 ? `${late} مهام متأخرة` : "التنبيهات"}>
        <Icon name="bell" className="size-[21px]" />
        {unread > 0 ? (
          <span className="tabular absolute top-0.5 right-0.5 grid h-[1.15rem] min-w-[1.15rem] place-items-center rounded-full bg-red px-1 text-[11px] font-semibold text-white ring-2 ring-top">{unread > 9 ? "9+" : unread}</span>
        ) : null}
      </button>
      {open ? (
        <div role="dialog" aria-label="التنبيهات" className="pop absolute top-[calc(100%+0.5rem)] left-0 z-30 grid w-[min(23rem,calc(100vw-2rem))] gap-1 rounded-[var(--radius-card)] p-1.5">
          <div className="flex items-center justify-between px-2.5 py-1.5">
            <span className="font-semibold">التنبيهات</span>
            {unread > 0 ? (
              <button type="button" className="text-[13px] font-medium text-accent-ink hover:underline" onClick={() => read.mutate(undefined)}>
                علّم الكل مقروء
              </button>
            ) : null}
          </div>
          {items.length === 0 ? (
            <p className="px-2 py-8 text-center text-[14px] text-ink-soft">مفيش تنبيهات.</p>
          ) : (
            <ul className="grid max-h-[60vh] gap-0.5 overflow-y-auto">
              {items.map((a) => (
                <li key={a.id}>
                  <button type="button" onClick={() => { if (!a.read) read.mutate([a.id]); setOpen(false); navigate(`/tasks/${a.task_id}`); }}
                    className={`grid w-full gap-0.5 rounded-[var(--radius-control)] px-2.5 py-2 text-start hover:bg-muted ${a.read ? "text-ink-soft" : ""}`}>
                    <span className="flex items-center gap-2 text-[14px] font-medium">
                      {!a.read ? <span className="size-2 shrink-0 rounded-full bg-red" aria-label="جديد" role="img" /> : null}
                      {label(ALERT_KINDS, a.kind)}
                      <span className="ms-auto text-[12px] font-normal text-ink-faint">{timeAgo(a.created_at, now)}</span>
                    </span>
                    <span className="truncate text-[13px] text-ink-soft">{label(TASK_TYPES, a.task_type)} - {a.client_name ?? "رقم من غير اسم"}</span>
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
      className={`flex min-h-9 w-full items-center gap-2.5 rounded-[var(--radius-control)] px-2.5 text-start text-[14px] hover:bg-muted ${danger ? "text-red-ink" : ""}`}>
      {children}
    </button>
  );
  return (
    <div ref={box} className="relative shrink-0">
      <button type="button" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-2 rounded-[10px] p-2 transition-colors hover:bg-muted sm:px-2.5 sm:py-1.5" aria-label={`${me.display_name} - ${label(ROLES, me.role)}`}>
        <Icon name="account" className="size-5 text-ink-soft sm:hidden" />
        <span className="hidden min-w-0 text-start leading-tight sm:grid">
          <span className="truncate text-[14px] font-medium">{me.display_name}</span>
          <span className="truncate text-[12px] text-ink-soft">{label(ROLES, me.role)}</span>
        </span>
        <Icon name="chevron-down" className="hidden size-4 text-ink-faint sm:block" />
      </button>
      {open ? (
        <div role="menu" className="pop absolute top-[calc(100%+0.5rem)] left-0 z-30 grid w-56 gap-0.5 rounded-[var(--radius-card)] p-1.5">
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

/** «الأحد 25 أكتوبر 2026 - 9:15 ص» (Cairo), on wide screens only. */
function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(t);
  }, []);
  return (
    <p className="hidden shrink-0 pe-2 text-[13.5px] text-ink-soft xl:block">
      {cairoLongDate(now)} - <span className="tabular">{cairoTime(now)}</span>
    </p>
  );
}

/** The search in the top bar opens the command palette; Ctrl K opens it from anywhere. */
function Search() {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen(true);
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, []);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} aria-label="بحث واختصارات" aria-haspopup="dialog"
        className="flex h-10 min-w-0 flex-1 items-center gap-2.5 rounded-[var(--radius-control)] border border-line bg-card-2 px-3 text-start transition-colors hover:border-line-strong sm:max-w-[26rem]">
        <Icon name="search" className="size-[18px] shrink-0 text-ink-faint" />
        <span className="min-w-0 flex-1 truncate text-[14.5px] text-ink-faint">دوّر على عميل، أو روح لأي صفحة</span>
        <kbd className="hidden shrink-0 rounded border border-line bg-card px-1.5 font-sans text-[12px] text-ink-faint md:block" dir="ltr">Ctrl K</kbd>
      </button>
      <CommandPalette open={open} onClose={() => setOpen(false)} />
    </>
  );
}
