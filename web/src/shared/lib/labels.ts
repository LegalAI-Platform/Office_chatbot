// Arabic names for the codes the API uses (DECISIONS v1.8: S-05, B-14, B-13, B-25).

export const TASK_TYPES: Record<string, string> = {
  existing_client_message: "موكّل حالي يستفسر عن قضيته",
  late_reschedule: "طلب متأخر لتغيير الموعد",
  refund: "استرداد",
  price_quote: "تسعير",
  discount_request: "طلب خصم",
  complaint: "شكوى",
  human_requested: "طلب التحدث مع موظف",
  safety_alert: "تنبيه أمان",
  lawyer_direct_message: "رسالة لقائمة المستشار",
  external_contact: "زميل أو صحافة أو مورّد",
  unclear_message: "رسالة غير واضحة",
  urgent_request: "طلب عاجل",
  service_request: "طلب خدمة",
  reschedule_or_cancel: "تغيير موعد أو إلغاؤه",
  booking_unavailable: "حجز: لا توجد مواعيد أو التقويم غير متاح",
  booking_cancelled: "عميل ألغى حجزه",
  booking_message_failed: "تعذّر وصول رسالة إلى العميل", // v1.44: a booking notice or any reply of Hazem's
  calendar_conflict: "حدث في التقويم يتعارض مع حجز",
  office_cancelled: "ألغى المكتب حجزًا: موعد آخر أو استرداد",
  service_pricing: "وصلت مستندات خدمة: حدّد السعر",
  service_unpaid: "انقضت مهلة دفع سعر خدمة",
  urf_review: "وصلت مستندات عقد عرفي: راجعها قبل الموعد",
  urf_documents_missing: "عقد عرفي: تبقّت 24 ساعة ولم تصل المستندات",
  urf_help: "عقد عرفي: يحتاج العميل مساعدة في الأسئلة",
  reminder_failed: "تعذّر وصول التذكير: ذكّر العميل هاتفيًا",
  office_info_missing: "سأل عميل عن معلومة غير مسجّلة عن المكتب: أضف العنوان والخريطة وبيانات الدفع (إعدادات المواعيد ← بيانات المكتب والدفع) أو أيام الاستشارات",
  service_question: "استفسار عميل عن خدمة (هل نقدّمها، أو تفاصيلها): رُدّ عليه",
  payment_review_due: "اقترب الموعد ولم يُراجَع التحويل بعد: راجعه الآن (الإيصالات والمدفوعات)",
  appointment_unmarked: "انقضى موعد دون تسجيل نتيجته، ولم يُراجَع التحويل بعد: راجعه وسجّل نتيجة الموعد (المواعيد)",
  rating_low: "قيّم عميل الخدمة بـ«يحتاج للتحسين»: تواصل معه (السبب في رسالة العميل إن كتبه)",
  urgent_call_failed: "تعذّر وصول تنبيه الاستشارة العاجلة إلى المستشار: تواصل معه أو مع العميل الآن (رقم المستشار في إعدادات المواعيد ← بيانات المكتب والدفع)",
  urgent_call_unanswered: "لم يردّ المستشار على تنبيه استشارة عاجلة مدفوعة (لم يضغط «التحويل تمام، هكلمه» ولا «فيه مشكلة»): تصرّف الآن",
  // v1.110: a paid urgent consultation's one task; the two above are its next steps (the same task)
  urgent_call: "استشارة عاجلة مدفوعة: تنبيه المستشار على واتساب",
};

/** The urgent tasks (v1.110: a paid urgent consultation's one task at each of its steps). */
export const URGENT_TYPES = new Set(["urgent_request", "safety_alert", "urgent_call", "urgent_call_failed", "urgent_call_unanswered"]);

/** TASK 12 (B-18): the reminders before a confirmed appointment. */
export const REMINDER_KINDS: Record<string, string> = {
  day_before: "تذكير قبل الموعد بيوم",
  two_hours: "تذكير قبل الموعد بساعتين",
};

/** Why a reminder never went out (appointment_reminders.skip_reason). */
export const REMINDER_SKIPS: Record<string, string> = {
  too_close: "الوقت قريب جدًا من الموعد",
  passed: "تأكّد الحجز بعد موعد التذكير",
  late: "فات موعد التذكير",
  off: "نصوص التذكير غير مفعّلة",
};

/** TASK 10 (v1.14): the written services and their order's states (S-07). */
export const SERVICE_KINDS: Record<string, string> = {
  document_review: "مراجعة عقد",
  contract_drafting: "صياغة عقد",
  formal_notice: "إنذار رسمي",
  written_opinion: "رأي قانوني مكتوب",
};

export const SERVICE_STATES: Record<string, string> = {
  collecting: "في انتظار المستندات",
  awaiting_price: "في انتظار السعر",
  awaiting_payment: "في انتظار الدفع",
  in_progress: "قيد التنفيذ",
  delivered: "تم التسليم",
  closed: "مغلق",
};

/** TASK 08: the services booked from the calendar (B-23) and the appointment's states (S-03). */
export const BOOKING_SERVICES: Record<string, string> = {
  consultation_phone_video: "هاتفيًا أو بالفيديو",
  consultation_office: "مقابلة في المكتب",
  consultation_urgent: "عاجلة هاتفيًا",
  urf_marriage_office: "عقد زواج عرفي (شهود المكتب)",
  urf_marriage_own: "عقد زواج عرفي (شهود العميل)",
  urf_divorce_office: "طلاق عرفي (شهود المكتب)",
  urf_divorce_own: "طلاق عرفي (شهود العميل)",
};

/** 1.25 (B-23): where the lawyer calls for a phone or video consultation. */
export const CALL_APPS: Record<string, string> = {
  whatsapp: "واتساب",
  facetime: "FaceTime",
  meet: "Google Meet",
  phone: "مكالمة هاتفية عادية", // v1.49: a normal phone call from the office's number
};

export const APPOINTMENT_STATES: Record<string, string> = {
  pending_payment: "في انتظار الإيصال",
  expired: "انتهى (لم يصل الإيصال)",
  confirmed: "مؤكد",
  completed: "تم",
  no_show: "لم يحضر",
  cancelled_by_client: "ألغاه العميل",
  cancelled_by_office: "ألغاه المكتب",
};

/** TASK 09: S-04. The system never says a payment is confirmed: «وصل إيصال» is the image only. */
export const PAYMENT_STATES: Record<string, string> = {
  requested: "لم يدفع بعد",
  proof_received: "وصل إيصال، في انتظار المراجعة",
  duplicate_suspected: "وصل إيصال، في انتظار المراجعة",
  verified: "تم التأكيد",
  partial: "مبلغ ناقص",
  not_received: "لم يصل",
  refund_requested: "استرداد مطلوب",
  refunded: "تم الاسترداد",
};

export const REFUND_REASONS: Record<string, string> = {
  client_within_24h: "ألغى العميل خلال 24 ساعة من الدفع",
  office_cancelled: "ألغى المكتب واختار العميل الاسترداد",
  overpaid: "دفع مبلغًا زائدًا",
};

export const TASK_STATES: Record<string, string> = {
  open: "مفتوحة",
  in_progress: "قيد التنفيذ",
  done: "مكتملة",
  overdue: "متأخرة",
  cancelled: "ملغاة",
};

export const CLIENT_TYPES: Record<string, string> = {
  new_client: "عميل جديد",
  existing_client: "موكّل حالي",
  lawyer_direct: "قائمة المستشار",
  blocked: "محظور",
  lawyer_colleague: "زميل محامٍ",
  press: "صحافة",
  vendor: "مورّد",
};

export const SOURCES: Record<string, string> = {
  ad: "إعلان",
  link: "رابط",
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
  urf_marriage: "زواج عرفي",
  urf_divorce: "طلاق عرفي",
  urf_proof: "إثبات عرفي",
};

export const ROLES: Record<string, string> = {
  maintenance: "الصيانة", // v1.100
  manager: "مدير",
  lawyer: "المستشار",
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
  interactive: "اختيار من قائمة",
  button: "زر",
  unsupported: "نوع غير مدعوم",
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
  task_overdue: "مهمة متأخرة",
  task_urgent: "طلب عاجل",
  task_human: "عميل يطلب التحدث مع موظف",
  task_rating: "تقييم ضعيف من عميل",
  system: "مشكلة في النظام",
  // v1.105: any other task Hazem opened for the office; the line under it says why (its type)
  task_new: "عميل يحتاج إلى موظف",
};

/** TASK 13: what a system alert is about. */
export const SYSTEM_WHAT: Record<string, string> = {
  backup: "فشلت النسخة الاحتياطية الليلية",
  healthcheck: "اكتشف الفحص اليومي خدمة متوقفة",
  drill: "فشلت تجربة استرجاع النسخة الاحتياطية",
  log_scan: "تحتوي السجلات على مفتاح أو رقم كامل",
  n8n: "توجد عمليات فاشلة في n8n",
  turn: "توقفت معالجة رسالة عميل بعد محاولات عديدة",
  ai: "الذكاء الاصطناعي الذي يفهم رسائل العملاء متوقف",
};

/** B-14: the office's lists. */
export const LIST_KINDS: Record<string, string> = {
  existing_client: "الموكّلون الحاليون",
  lawyer_direct: "قائمة المستشار المباشرة",
  blocked: "المحظورون",
};

/** TASK 11 (v1.15): the Urfi cases (S-06) and their answers (B-11). */
export const URF_KINDS: Record<string, string> = { marriage: "عقد زواج عرفي", divorce: "طلاق عرفي" };

export const URF_STATES: Record<string, string> = {
  collecting: "يجيب عن الأسئلة",
  documents_pending: "يختار موعدًا",
  booked: "محجوز: راجع المستندات",
  approved: "وافق المستشار",
  lawyer_rejected: "رفض المستشار",
  rejected_age: "مغلق: السن أقل من 18",
  rejected_guardian: "مغلق: لا يوجد ولي",
  redirected_consultation: "حُوِّل إلى استشارة",
  abandoned: "مغلق: توقف العميل عن الرد",
  completed: "مغلق: تم الموعد", // v1.99: its appointment was marked done
};

export const URF_ANSWERS: Record<string, string> = {
  first: "الزواج الأول", divorced: "مطلقة أو مختلعة", khul: "مختلعة سابقًا", widow: "أرملة",
  father: "الأب", brother: "الأخ", uncle: "العم", none: "لا يوجد",
  office: "من المكتب", own: "من طرف العميل",
};
