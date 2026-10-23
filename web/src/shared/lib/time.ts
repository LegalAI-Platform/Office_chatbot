// Every time is shown in Africa/Cairo (T-12), with plain 0-9 digits, whatever the device's zone.

const CAIRO = "Africa/Cairo";

const timeFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, hour: "numeric", minute: "2-digit" });
const dayFmt = new Intl.DateTimeFormat("en-CA", { timeZone: CAIRO, year: "numeric", month: "2-digit", day: "2-digit" });
const dateFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, day: "numeric", month: "long" });

const cairoDay = (d: Date): string => dayFmt.format(d);

/** «النهارده 10:00 ص» / «بكرة 10:00 ص» / «امبارح …» / «23 أكتوبر 10:00 ص». */
export function cairoWhen(iso: string, now: Date = new Date()): string {
  const d = new Date(iso);
  const time = timeFmt.format(d);
  const day = cairoDay(d);
  const today = cairoDay(now);
  const tomorrow = cairoDay(new Date(now.getTime() + 86_400_000));
  const yesterday = cairoDay(new Date(now.getTime() - 86_400_000));
  if (day === today) return `النهارده ${time}`;
  if (day === tomorrow) return `بكرة ${time}`;
  if (day === yesterday) return `امبارح ${time}`;
  return `${dateFmt.format(d)} ${time}`;
}

export const isPast = (iso: string, now: Date = new Date()): boolean => new Date(iso).getTime() < now.getTime();

const longDateFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, weekday: "long", day: "numeric", month: "long", year: "numeric" });
const monthFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, month: "long", year: "numeric" });
const weekdayFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, weekday: "long" });
const dayNumFmt = new Intl.DateTimeFormat("en-US", { timeZone: CAIRO, day: "numeric" });
const hourFmt = new Intl.DateTimeFormat("en-US", { timeZone: CAIRO, hour: "numeric", hourCycle: "h23" });

/** «السبت، 25 أكتوبر 2026» (the comma Intl adds after the weekday is dropped, as on the sheet). */
export const cairoLongDate = (d: Date): string => longDateFmt.format(d).replace("،", "");
export const cairoMonth = (d: Date): string => monthFmt.format(d);
export const cairoTime = (d: Date): string => timeFmt.format(d);
export const cairoHour = (d: Date): number => Number(hourFmt.format(d)) % 24;

/** The Cairo week that holds `now`, Saturday first (the office week), with today marked. */
export function cairoWeek(now: Date = new Date()): { key: string; weekday: string; day: string; today: boolean }[] {
  const today = cairoDay(now);
  const back = (["Sat", "Sun", "Mon", "Tue", "Wed", "Thu", "Fri"] as const).indexOf(
    new Intl.DateTimeFormat("en-US", { timeZone: CAIRO, weekday: "short" }).format(now) as "Sat",
  );
  return Array.from({ length: 7 }, (_, i) => {
    const d = new Date(now.getTime() + (i - back) * 86_400_000);
    return { key: cairoDay(d), weekday: weekdayFmt.format(d), day: dayNumFmt.format(d), today: cairoDay(d) === today };
  });
}

/** «منذ 10 دقائق» / «منذ ساعتين» / «منذ 3 أيام». */
export function timeAgo(iso: string, now: Date = new Date()): string {
  const min = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 60_000));
  if (min < 1) return "دلوقتي";
  if (min < 60) return min === 1 ? "منذ دقيقة" : min === 2 ? "منذ دقيقتين" : `منذ ${min} ${min <= 10 ? "دقائق" : "دقيقة"}`;
  const h = Math.round(min / 60);
  if (h < 24) return h === 1 ? "منذ ساعة" : h === 2 ? "منذ ساعتين" : `منذ ${h} ${h <= 10 ? "ساعات" : "ساعة"}`;
  const d = Math.round(h / 24);
  return d === 1 ? "منذ يوم" : d === 2 ? "منذ يومين" : `منذ ${d} ${d <= 10 ? "أيام" : "يوم"}`;
}

const shortDateFmt = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: CAIRO, day: "numeric", month: "long" });

/** «اليوم» / «أمس» / «غداً» / «26 أكتوبر», as the mockup's due column. */
export function cairoDayWord(iso: string, now: Date = new Date()): string {
  const day = cairoDay(new Date(iso));
  if (day === cairoDay(now)) return "اليوم";
  if (day === cairoDay(new Date(now.getTime() - 86_400_000))) return "أمس";
  if (day === cairoDay(new Date(now.getTime() + 86_400_000))) return "غداً";
  return shortDateFmt.format(new Date(iso));
}
