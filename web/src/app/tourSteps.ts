// The panel's tour (v1.102, the owner's steps and words of 2026-11-12): bubbles over the real panel, one part of the
// screen each, for every account the first time it opens the panel, and again from «اعرض الجولة تاني». Which bubbles an
// account sees (only the pages its role reaches) is decided here, where a bubble goes in tourPlace.ts; app/Tour.tsx draws them.
import { can } from "../shared/api/session";
import type { Me } from "../shared/api/types";
import type { IconName } from "../shared/ui/Icon";

/** «اعرض الجولة تاني» (the account menu and «الإعدادات»): the home page with this opens the tour from its first step. */
export const TOUR_REPLAY = "/?tour=1";

/**
 * What a bubble's words may depend on: `wide`, a screen with a keyboard's shortcuts (the Ctrl K hint is said only there);
 * `target`, the data-tour name it points at now (null: none, the middle of the screen).
 */
export interface TourScreen {
  wide: boolean;
  target: string | null;
}

export interface TourStep {
  id: string;
  title: string;
  body: (on: TourScreen) => string;
  /** "seal": the office's seal (the welcome). */
  icon: IconName | "seal";
  /**
   * What it points at: `data-tour` names, the first one on the screen wins (the side bar's link on a computer, the tab
   * bar's on a phone). None on the screen, or none given: the bubble sits in the middle and points at nothing.
   */
  targets: string[];
  /** The main button's word instead of «التالي». */
  next?: string;
}

const firstName = (name: string) => name.trim().split(/\s+/)[0] ?? "";

/** The steps for this account, in order: a page its role cannot open has no bubble. */
export function tourSteps(me: Me): TourStep[] {
  const name = firstName(me.display_name);
  const settings = [
    "حسابك",
    "وكلمة السر",
    ...(can(me, "users.manage") ? ["وحسابات المكتب"] : []),
    ...(can(me, "lists.manage") ? ["وقوايم المكتب"] : []),
    ...(can(me, "texts.manage") ? ["ونصوص حازم وأدوات التجربة"] : []),
    "والأسعار",
  ];
  const steps: (TourStep | false)[] = [
    {
      id: "welcome", icon: "seal", targets: [], next: "يلا نبدأ",
      title: name ? `أهلًا بيك يا ${name}` : "أهلًا بيك",
      body: () => "دي جولة سريعة تعرّفك على لوحة المكتب في أقل من دقيقة. تقدر تتخطاها في أي وقت.",
    },
    {
      id: "kpis", icon: "trend-up", targets: ["home-kpis"], title: "أرقام النهارده",
      body: () => "مواعيد اليوم، والإيصالات اللي مستنية تأكيد، والأمور العاجلة، والمهام المتأخرة. اضغط أي كارت يفتح تفاصيله.",
    },
    {
      id: "tasks", icon: "tasks", targets: ["nav-tasks"], title: "الطلبات / المهام",
      body: () => "لما عميل على واتساب يحتاج حد من المكتب، حازم بيفتح له مهمة هنا. "
        + (can(me, "tasks.assign") ? "وتقدر تعيّن مين يشتغل عليها." : "اضغط «هاشتغل عليها»، ولما تخلّص «خلصت»."),
    },
    can(me, "schedule.manage") && {
      id: "appointments", icon: "calendar", targets: ["nav-appointments"], title: "المواعيد",
      body: () => "كل حجوزات العملاء يوم بيوم. ومن هنا كمان تقفل يوم أو تغيّر ساعات الشغل.",
    },
    {
      id: "chats", icon: "chat", targets: ["nav-chats"], title: "المحادثات",
      body: () => "هنا تقرا شات العملاء على واتساب وترد عليهم بنفسك. بتفتح في Chatwoot.",
    },
    (can(me, "schedule.manage") || can(me, "services.manage") || can(me, "urf.manage")) && {
      // a phone has no side bar: the tab bar's «المزيد», whose sheet holds these pages (and says so, the owner's words)
      id: "money", icon: "receipt", targets: ["nav-money", "nav-more"], title: "الفلوس والخدمات",
      body: (on) => "الإيصالات: تتأكد إن التحويل وصل. طلبات الخدمات: عقد أو إنذار أو رأي. حالات العرفي: الورق والميعاد."
        + (on.target === "nav-more" ? " وتلاقيهم كلهم في «المزيد»." : ""),
    },
    can(me, "reports.view") && {
      // a phone: the home page's «الشهر ده», whose «عرض الكل» opens the reports
      id: "reports", icon: "reports", targets: ["nav-reports", "home-month"], title: "التقارير",
      body: () => "أرقام الشهر: المهام اللي خلصت، والعملاء الجداد، والحجوزات والفلوس، وتقييم العملاء.",
    },
    {
      id: "bell", icon: "bell", targets: ["bell"], title: "التنبيهات",
      body: () => "الجرس بيقولك على الجديد والعاجل والمتأخر. ومنه تشغّل الإشعارات علشان توصلك وإنت في تاب تاني.",
    },
    {
      id: "search", icon: "search", targets: ["search"], title: "البحث",
      body: (on) => "دوّر في المهام باسم العميل أو بآخر أرقام موبايله." + (on.wide ? " وCtrl K بيفتح اختصارات لكل صفحة." : ""),
    },
    {
      // a phone: «المزيد» too, whose sheet ends with «الإعدادات» (v1.101: no tab of its own), and it says so
      id: "settings", icon: "settings", targets: ["nav-settings", "nav-more"], title: "الإعدادات",
      body: (on) => `فيها ${settings.join("، ")}.` + (on.target === "nav-more" ? " وعلى الموبايل هتلاقيها في آخر «المزيد»." : ""),
    },
    {
      id: "account", icon: "account", targets: ["account-menu"], title: "حسابك", next: "تمام",
      body: () => "من هنا حسابك، وكلمة السر، والشكل فاتح أو غامق، والخروج. وعايز الجولة دي تاني؟ اضغط «اعرض الجولة تاني» من هنا.",
    },
  ];
  return steps.filter((s): s is TourStep => !!s);
}
