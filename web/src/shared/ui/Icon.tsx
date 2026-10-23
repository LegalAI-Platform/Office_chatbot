// One family of line icons (24px grid, 1.8px stroke, round caps), drawn here so the panel needs no
// icon library and no emoji. The set follows the owner's brand sheet. Icons that point along the
// reading direction are drawn for LTR and flip in RTL.

export type IconName =
  | "home"
  | "clients"
  | "tasks"
  | "calendar"
  | "chat"
  | "document"
  | "reports"
  | "settings"
  | "users"
  | "account"
  | "back"
  | "forward"
  | "chevron-down"
  | "chevron-start"
  | "chevron-end"
  | "logout"
  | "search"
  | "bell"
  | "check"
  | "check-circle"
  | "alert"
  | "alert-circle"
  | "info"
  | "clock"
  | "receipt"
  | "more"
  | "play"
  | "x"
  | "user-plus"
  | "key"
  | "lock"
  | "refresh"
  | "sun"
  | "moon"
  | "trend-up"
  | "message"
  | "whatsapp";

const PATHS: Record<IconName, string[]> = {
  home: ["M3 10.5 12 3l9 7.5", "M5 9.5V20a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V9.5"],
  clients: ["M15 19v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 3 17.5V19", "M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z", "M21 19v-1.5a3.5 3.5 0 0 0-2.5-3.35", "M15.5 4.2a3.5 3.5 0 0 1 0 6.6"],
  tasks: ["M9 4h6v3H9z", "M15 5.5h2a2 2 0 0 1 2 2V19a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7.5a2 2 0 0 1 2-2h2", "m9 14 2 2 4-4"],
  calendar: ["M4 6.5A1.5 1.5 0 0 1 5.5 5h13A1.5 1.5 0 0 1 20 6.5v12a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 18.5z", "M4 10h16", "M8 3v4M16 3v4", "M8 14h2M14 14h2M8 17h2"],
  chat: ["M20.5 12a8 8 0 0 1-11.7 7.1L4 20l1.1-4.4A8 8 0 1 1 20.5 12z", "M8.5 12h.01M12.5 12h.01M16.5 12h.01"],
  document: ["M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z", "M14 3v5h5", "M9 13h6M9 17h6"],
  reports: ["M6 20V12", "M10 20V6", "M14 20v-9", "M18 20V4", "M4 20h16"],
  settings: ["M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z", "M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"],
  users: ["M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2", "M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M22 21v-2a4 4 0 0 0-3-3.87", "M16 3.13a4 4 0 0 1 0 7.75"],
  account: ["M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M4 21a8 8 0 0 1 16 0"],
  // drawn for LTR: back points left, forward points right (both flip in RTL)
  back: ["M19 12H5", "m11 6-6 6 6 6"],
  forward: ["M5 12h14", "m13 6 6 6-6 6"],
  "chevron-down": ["m6 9 6 6 6-6"],
  "chevron-start": ["m15 6-6 6 6 6"],
  "chevron-end": ["m9 6 6 6-6 6"],
  logout: ["M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "m16 17 5-5-5-5", "M21 12H9"],
  search: ["M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "m20 20-3.5-3.5"],
  bell: ["M6 8a6 6 0 1 1 12 0c0 7 3 8 3 8H3s3-1 3-8", "M10.3 20a1.9 1.9 0 0 0 3.4 0"],
  check: ["M20 6 9 17l-5-5"],
  "check-circle": ["M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z", "m8.5 12 2.5 2.5 4.5-5"],
  alert: ["M12 3 2 20h20z", "M12 10v4M12 17h.01"],
  "alert-circle": ["M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z", "M12 7.5v5M12 16h.01"],
  info: ["M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z", "M12 11v5M12 8h.01"],
  clock: ["M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z", "M12 7v5l3 2"],
  receipt: ["M3 7a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z", "M3 10h18", "M7 15h3"],
  more: ["M6 12h.01M12 12h.01M18 12h.01"],
  play: ["M6 4l14 8-14 8z"],
  x: ["M18 6 6 18", "M6 6l12 12"],
  "user-plus": ["M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2", "M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M19 8v6", "M22 11h-6"],
  key: ["M15 7a4 4 0 1 1-3.9 4.9L3 20v-3h3v-3h3l2.1-2.1A4 4 0 0 1 15 7z"],
  lock: ["M7 11V7a5 5 0 0 1 10 0v4", "M5 11h14v10H5z"],
  refresh: ["M21 12a9 9 0 0 1-15.5 6.2L3 16", "M3 12A9 9 0 0 1 18.5 5.8L21 8", "M21 3v5h-5", "M3 21v-5h5"],
  sun: ["M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", "M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"],
  moon: ["M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"],
  "trend-up": ["m3 17 6-6 4 4 8-8", "M15 7h6v6"],
  message: ["M4 5h16v11H8l-4 4z"],
  whatsapp: ["M20.5 11.7a8.5 8.5 0 0 1-12.6 7.4L3.5 20.5l1.4-4.2a8.5 8.5 0 1 1 15.6-4.6z", "M9 8.5c0 3.5 3 6.5 6.5 6.5l1-1.5-2-1-1 .8a4.5 4.5 0 0 1-2.8-2.8l.8-1-1-2z"],
};

const DIRECTIONAL: ReadonlySet<IconName> = new Set(["back", "forward", "logout", "chevron-start", "chevron-end"]);

export function Icon({ name, className = "size-5", label }: { name: IconName; className?: string; label?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={`${className} ${DIRECTIONAL.has(name) ? "rtl:-scale-x-100" : ""}`}
      aria-hidden={label ? undefined : true}
      role={label ? "img" : undefined}
      aria-label={label}
      focusable="false"
    >
      {PATHS[name].map((d) => (
        <path key={d} d={d} />
      ))}
    </svg>
  );
}
