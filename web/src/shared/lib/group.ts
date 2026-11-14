// fix/alert-flood (the owner: «لو عميل جه وحجز بتلاقي فيه مليون حاجه»; the tester: «a ton of notifications for one
// client, it needs to be cleaner»): one client's alerts and tasks are one row, which opens to show each of them.
import { label, TASK_TYPES } from "./labels";

export interface ClientGroup<T> { key: string; items: T[] }

/**
 * One client's items together, in the order of each client's first item (the lists come newest first). An item with no
 * client key (a number with no name, a system problem) stays alone, so two different clients are never merged.
 */
export function groupByClient<T>(items: T[], keyOf: (t: T) => string | undefined): ClientGroup<T>[] {
  const groups: ClientGroup<T>[] = [];
  const at = new Map<string, ClientGroup<T>>();
  items.forEach((t, i) => {
    const key = keyOf(t);
    const had = key ? at.get(key) : undefined;
    if (had) { had.items.push(t); return; }
    const g = { key: key ?? `alone:${i}`, items: [t] };
    groups.push(g);
    if (key) at.set(key, g);
  });
  return groups;
}

/** A task kind in a few words for a list of them: the part before its explanation («…: راجعه الآن (…)»). */
export const shortType = (type: string | undefined): string => label(TASK_TYPES, type ?? "").split(/[:(]/)[0]!.trim();

/** A noun's forms after a number: «عنصر واحد» (1), «عنصران» (2), «عناصر» (3 to 10), «عنصرًا» (11 to 99), «عنصر» (100, 101…). */
export interface CountForms { one: string; two: string; few: string; many: string; hundred?: string }

/** A count in Arabic number agreement: «عنصر واحد»، «عنصران»، «3 عناصر»، «11 عنصرًا»، «100 عنصر». */
export function countWords(n: number, w: CountForms): string {
  if (n === 1) return w.one;
  if (n === 2) return w.two;
  const r = n % 100;
  if (r >= 3 && r <= 10) return `${n} ${w.few}`;
  if (r >= 11) return `${n} ${w.many}`;
  return `${n} ${w.hundred ?? w.many}`;
}

export const THINGS: CountForms = { one: "عنصر واحد", two: "عنصران", few: "عناصر", many: "عنصرًا", hundred: "عنصر" };
export const NEW_THINGS: CountForms = { one: "عنصر جديد", two: "عنصران جديدان", few: "عناصر جديدة", many: "عنصرًا جديدًا", hundred: "عنصر جديد" };
export const NEW_ALERTS: CountForms = { one: "تنبيه جديد", two: "تنبيهان جديدان", few: "تنبيهات جديدة", many: "تنبيهًا جديدًا", hundred: "تنبيه جديد" };
export const MORE_ALERTS: CountForms = { one: "تنبيه جديد آخر", two: "تنبيهان جديدان آخران", few: "تنبيهات جديدة أخرى", many: "تنبيهًا جديدًا آخر", hundred: "تنبيه جديد آخر" };
export const LATE_TASKS: CountForms = { one: "مهمة متأخرة", two: "مهمتان متأخرتان", few: "مهام متأخرة", many: "مهمة متأخرة" };
/** v1.112: the client's messages one task holds (his later ones join it). */
export const MESSAGES: CountForms = { one: "رسالة واحدة", two: "رسالتان", few: "رسائل", many: "رسالة", hundred: "رسالة" };
/** after «في»: «في محادثة واحدة»، «في محادثتين»، «في 3 محادثات». */
export const CONVERSATIONS: CountForms = { one: "محادثة واحدة", two: "محادثتين", few: "محادثات", many: "محادثة" };

/** «عنصر واحد»، «عنصران»، «3 عناصر»، «11 عنصرًا». */
export const thingsWords = (n: number): string => countWords(n, THINGS);

const OTHER_TASKS: CountForms = { one: "مهمة أخرى", two: "مهمتان أخريان", few: "مهام أخرى", many: "مهمة أخرى", hundred: "مهمة أخرى" };

/**
 * One line for a client's several tasks: the first one's kind (the lists put the most pressing first) and how many more,
 * «تعذّر وصول رسالة إلى العميل ومهمتان أخريان», never a long list cut with «…».
 */
export const firstAndMore = (types: (string | undefined)[]): string =>
  types.length <= 1 ? shortType(types[0]) : `${shortType(types[0])} و${countWords(types.length - 1, OTHER_TASKS)}`;
