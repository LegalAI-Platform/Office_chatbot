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
    ...(can(me, "lists.manage") ? ["وقوائم المكتب"] : []),
    ...(can(me, "texts.manage") ? ["ونصوص حازم وأدوات التجربة"] : []),
    "والأسعار",
  ];
  const steps: (TourStep | false)[] = [
    {
      id: "welcome", icon: "seal", targets: [], next: "لنبدأ",
      title: name ? `مرحبًا بك يا ${name}` : "مرحبًا بك",
      body: () => "هذه جولة سريعة تعرّفك بلوحة المكتب في أقل من دقيقة. يمكنك تخطّيها في أي وقت.",
    },
    {
      id: "kpis", icon: "trend-up", targets: ["home-kpis"], title: "أرقام اليوم",
      body: () => "مواعيد اليوم، والإيصالات في انتظار التأكيد، والأمور العاجلة، والمهام المتأخرة. اضغط أي بطاقة لعرض تفاصيلها.",
    },
    {
      id: "tasks", icon: "tasks", targets: ["nav-tasks"], title: "الطلبات / المهام",
      body: () => "عندما يحتاج عميل على واتساب إلى أحد من المكتب، يفتح له حازم مهمة هنا. "
        + (can(me, "tasks.assign") ? "ويمكنك تحديد من يعمل عليها." : "اضغط «ابدأ العمل عليها»، وعند الانتهاء «تحديد كمكتملة»."),
    },
    can(me, "schedule.manage") && {
      id: "appointments", icon: "calendar", targets: ["nav-appointments"], title: "المواعيد",
      body: () => "جميع حجوزات العملاء يومًا بيوم. ومن هنا أيضًا يمكنك إغلاق يوم أو تغيير ساعات العمل.",
    },
    {
      id: "chats", icon: "chat", targets: ["nav-chats"], title: "المحادثات",
      body: () => "هنا تقرأ محادثات العملاء على واتساب وترد عليهم بنفسك. تُفتح في Chatwoot.",
    },
    (can(me, "schedule.manage") || can(me, "services.manage") || can(me, "urf.manage")) && {
      // a phone has no side bar: the tab bar's «المزيد», whose sheet holds these pages (and says so, the owner's words)
      id: "money", icon: "receipt", targets: ["nav-money", "nav-more"], title: "المالية والخدمات",
      body: (on) => "الإيصالات: للتأكد من وصول التحويل. طلبات الخدمات: عقد أو إنذار أو رأي. حالات العرفي: المستندات والموعد."
        + (on.target === "nav-more" ? " وتجدها جميعًا في «المزيد»." : ""),
    },
    can(me, "reports.view") && {
      // a phone: the home page's «الشهر ده», whose «عرض الكل» opens the reports
      id: "reports", icon: "reports", targets: ["nav-reports", "home-month"], title: "التقارير",
      body: () => "أرقام الشهر: المهام المكتملة، والعملاء الجدد، والحجوزات والمبالغ، وتقييم العملاء.",
    },
    {
      id: "bell", icon: "bell", targets: ["bell"], title: "التنبيهات",
      body: () => "يُعلمك الجرس بالجديد والعاجل والمتأخر. ومنه تفعّل الإشعارات لتصلك وأنت في تبويب آخر.",
    },
    {
      id: "search", icon: "search", targets: ["search"], title: "البحث",
      body: (on) => "ابحث في المهام باسم العميل أو بآخر أرقام هاتفه." + (on.wide ? " ويفتح Ctrl K اختصارات لكل صفحة." : ""),
    },
    {
      // a phone: «المزيد» too, whose sheet ends with «الإعدادات» (v1.101: no tab of its own), and it says so
      id: "settings", icon: "settings", targets: ["nav-settings", "nav-more"], title: "الإعدادات",
      body: (on) => `فيها ${settings.join("، ")}.` + (on.target === "nav-more" ? " وعلى الهاتف تجدها في آخر «المزيد»." : ""),
    },
    {
      id: "account", icon: "account", targets: ["account-menu"], title: "حسابك", next: "إنهاء",
      body: () => "من هنا تصل إلى حسابك، وكلمة السر، والمظهر الفاتح أو الداكن، وتسجيل الخروج. ولعرض هذه الجولة مرة أخرى، اضغط «إعادة عرض الجولة» من هنا.",
    },
  ];
  return steps.filter((s): s is TourStep => !!s);
}
