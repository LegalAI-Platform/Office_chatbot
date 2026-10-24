// Arabic names for the codes the API uses (DECISIONS v1.8: S-05, B-14, B-13, B-25).

export const TASK_TYPES: Record<string, string> = {
  existing_client_message: "موكل حالي بيسأل عن موضوعه",
  late_reschedule: "تغيير ميعاد متأخر",
  refund: "استرداد",
  price_quote: "تسعير",
  discount_request: "طلب خصم",
  complaint: "شكوى",
  human_requested: "طلب يكلم موظف",
  safety_alert: "تنبيه أمان",
  lawyer_direct_message: "رسالة لقايمة الأستاذ",
  external_contact: "زميل أو صحافة أو مورّد",
  unclear_message: "رسالة مش واضحة",
  urgent_request: "طلب عاجل",
  service_request: "طلب خدمة",
  reschedule_or_cancel: "تغيير أو إلغاء ميعاد",
  booking_unavailable: "حجز: مفيش مواعيد أو الكالندر مش متاح",
  booking_cancelled: "عميل لغى حجزه",
  booking_message_failed: "رسالة حجز ما وصلتش للعميل",
  calendar_conflict: "حدث في الكالندر فوق حجز",
};

/** TASK 08: the services booked from the calendar (B-23) and the appointment's states (S-03). */
export const BOOKING_SERVICES: Record<string, string> = {
  consultation_phone_video: "تليفون أو فيديو",
  consultation_office: "مقابلة في المكتب",
  consultation_urgent: "عاجلة بالتليفون",
};

export const APPOINTMENT_STATES: Record<string, string> = {
  confirmed: "مؤكد",
  completed: "خلص",
  no_show: "ماحضرش",
  cancelled_by_client: "العميل لغى",
  cancelled_by_office: "المكتب لغى",
};

export const TASK_STATES: Record<string, string> = {
  open: "مفتوحة",
  in_progress: "شغال عليها",
  done: "خلصت",
  overdue: "متأخرة",
  cancelled: "اتلغت",
};

export const CLIENT_TYPES: Record<string, string> = {
  new_client: "عميل جديد",
  existing_client: "موكل حالي",
  lawyer_direct: "قايمة الأستاذ",
  blocked: "ممنوع",
  lawyer_colleague: "زميل محامي",
  press: "صحافة",
  vendor: "مورّد",
};

export const SOURCES: Record<string, string> = {
  ad: "إعلان",
  link: "لينك",
  direct: "مباشر",
};

export const MATTER_TYPES: Record<string, string> = {
  personal_status: "أحوال شخصية",
  inheritance: "ميراث",
  rent: "إيجارات",
  real_estate: "عقارات وملكية",
  criminal: "جنائي",
  cheques_trust_receipts: "شيكات وإيصالات أمانة",
  labor: "عمل وعمال",
  commercial: "تجاري وشركات",
  civil_compensation: "مدني وتعويضات",
  urfi: "عرفي",
  unclear: "غير واضح",
};

export const MATTER_SUBTYPES: Record<string, string> = {
  divorce: "طلاق",
  khula: "خلع",
  alimony: "نفقة",
  custody: "حضانة",
  visitation: "رؤية",
  marriage: "زواج",
  urf_marriage: "جواز عرفي",
  urf_divorce: "طلاق عرفي",
  urf_proof: "إثبات عرفي",
};

export const ROLES: Record<string, string> = {
  manager: "مدير",
  lawyer: "الأستاذ",
  staff: "موظف",
};

export const MEDIA_TYPES: Record<string, string> = {
  image: "صورة",
  document: "مستند",
  audio: "رسالة صوتية",
  video: "فيديو",
  sticker: "ملصق",
  location: "موقع",
  reaction: "تفاعل",
  interactive: "اختيار من قايمة",
  button: "زرار",
  unsupported: "نوع مش مدعوم",
};

export const label = (map: Record<string, string>, code: string | undefined | null): string =>
  code ? (map[code] ?? code) : "";

export function matterLabel(type?: string, subtype?: string): string {
  if (!type) return "غير معروف";
  const main = label(MATTER_TYPES, type);
  return subtype ? `${main} / ${label(MATTER_SUBTYPES, subtype)}` : main;
}

/** TASK 07c: the bell's alerts. */
export const ALERT_KINDS: Record<string, string> = {
  task_overdue: "مهمة اتأخرت",
  task_urgent: "طلب عاجل",
  task_human: "عميل عايز يكلم حد",
};

/** B-14: the office's lists. */
export const LIST_KINDS: Record<string, string> = {
  existing_client: "الموكلين الحاليين",
  lawyer_direct: "قايمة الأستاذ المباشرة",
  blocked: "الممنوعين",
};
