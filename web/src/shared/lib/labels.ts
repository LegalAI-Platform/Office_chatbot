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
  office_cancelled: "المكتب لغى حجز: ميعاد تاني أو استرداد",
  service_pricing: "ورق خدمة وصل: حدّد السعر",
  service_unpaid: "سعر خدمة عدّى من غير دفع",
  urf_review: "ورق عرفي وصل: راجعه قبل الميعاد",
  urf_documents_missing: "عرفي: فاضل 24 ساعة ومفيش ورق",
  urf_help: "عرفي: محتاج مساعدة في الأسئلة",
  reminder_failed: "تذكير ما وصلش: فكّر العميل بالتليفون",
  office_info_missing: "عميل سأل عن المكتب ومعلومته مش مكتوبة: اكتب العنوان والخريطة وأرقام الدفع (المواعيد ← بيانات المكتب) أو أيام الاستشارات",
  service_question: "عميل بيسأل المكتب بيعمل خدمة معيّنة ولا لأ: رد عليه",
  payment_review_due: "الميعاد قرّب والتحويل لسه ما اتراجعش: راجعه دلوقتي (الإيصالات والمدفوعات)",
};

/** TASK 12 (B-18): the reminders before a confirmed appointment. */
export const REMINDER_KINDS: Record<string, string> = {
  day_before: "تذكير اليوم اللي قبله",
  two_hours: "تذكير قبلها بساعتين",
};

/** Why a reminder never went out (appointment_reminders.skip_reason). */
export const REMINDER_SKIPS: Record<string, string> = {
  too_close: "الوقت قريب من الميعاد",
  passed: "الحجز اتأكد بعد وقته",
  late: "فات وقته",
  off: "نصوص التذكير مش متفعّلة",
};

/** TASK 10 (v1.14): the written services and their order's states (S-07). */
export const SERVICE_KINDS: Record<string, string> = {
  document_review: "مراجعة عقد",
  contract_drafting: "صياغة عقد",
  formal_notice: "إنذار رسمي",
  written_opinion: "رأي قانوني مكتوب",
};

export const SERVICE_STATES: Record<string, string> = {
  collecting: "مستني الورق",
  awaiting_price: "مستني السعر",
  awaiting_payment: "مستني الدفع",
  in_progress: "شغال",
  delivered: "اتسلّم",
  closed: "اتقفل",
};

/** TASK 08: the services booked from the calendar (B-23) and the appointment's states (S-03). */
export const BOOKING_SERVICES: Record<string, string> = {
  consultation_phone_video: "تليفون أو فيديو",
  consultation_office: "مقابلة في المكتب",
  consultation_urgent: "عاجلة بالتليفون",
  urf_marriage_office: "عقد عرفي (شهود المكتب)",
  urf_marriage_own: "عقد عرفي (شهودهم)",
  urf_divorce_office: "طلاق عرفي (شهود المكتب)",
  urf_divorce_own: "طلاق عرفي (شهودهم)",
};

/** 1.25 (B-23): where the lawyer calls for a phone or video consultation. */
export const CALL_APPS: Record<string, string> = {
  whatsapp: "واتساب",
  facetime: "FaceTime",
  meet: "Google Meet",
};

export const APPOINTMENT_STATES: Record<string, string> = {
  pending_payment: "مستني الإيصال",
  expired: "اتفك (الإيصال ما وصلش)",
  confirmed: "مؤكد",
  completed: "خلص",
  no_show: "ماحضرش",
  cancelled_by_client: "العميل لغى",
  cancelled_by_office: "المكتب لغى",
};

/** TASK 09: S-04. The system never says a payment is confirmed: «وصل إيصال» is the image only. */
export const PAYMENT_STATES: Record<string, string> = {
  requested: "لسه ما دفعش",
  proof_received: "وصل إيصال، مستني المراجعة",
  duplicate_suspected: "وصل إيصال، مستني المراجعة",
  verified: "اتأكد",
  partial: "ناقص",
  not_received: "ما وصلش",
  refund_requested: "استرداد مطلوب",
  refunded: "اترد",
};

export const REFUND_REASONS: Record<string, string> = {
  client_within_24h: "العميل لغى خلال 24 ساعة من الدفع",
  office_cancelled: "المكتب لغى والعميل اختار الاسترداد",
  overpaid: "دفع زيادة",
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
  system: "مشكلة في السيستم",
};

/** TASK 13: what a system alert is about. */
export const SYSTEM_WHAT: Record<string, string> = {
  backup: "النسخة الاحتياطية بتاعة الليلة فشلت",
  healthcheck: "الفحص اليومي لقى حاجة مش شغالة",
  drill: "تجربة استرجاع النسخة الاحتياطية فشلت",
  log_scan: "السجلات فيها مفتاح أو رقم كامل",
  n8n: "فيه حاجات في n8n فشلت",
  turn: "رسالة عميل وقفت بعد محاولات كتير",
  ai: "الـ AI اللي بيفهم رسايل العملاء مش شغال",
};

/** B-14: the office's lists. */
export const LIST_KINDS: Record<string, string> = {
  existing_client: "الموكلين الحاليين",
  lawyer_direct: "قايمة الأستاذ المباشرة",
  blocked: "الممنوعين",
};

/** TASK 11 (v1.15): the Urfi cases (S-06) and their answers (B-11). */
export const URF_KINDS: Record<string, string> = { marriage: "عقد جواز عرفي", divorce: "طلاق عرفي" };

export const URF_STATES: Record<string, string> = {
  collecting: "بيجاوب على الأسئلة",
  documents_pending: "بيختار ميعاد",
  booked: "محجوز: راجع الورق",
  approved: "الأستاذ وافق",
  lawyer_rejected: "الأستاذ رفض",
  rejected_age: "اتقفل: السن أقل من 18",
  rejected_guardian: "اتقفل: مفيش ولي",
  redirected_consultation: "اتحوّل لاستشارة",
};

export const URF_ANSWERS: Record<string, string> = {
  first: "أول جوازة", divorced: "اتطلقت قبل كده", khul: "اختلعت قبل كده",
  father: "الأب", brother: "الأخ", uncle: "العم", none: "مفيش",
  office: "من المكتب", own: "من طرفهم",
};
