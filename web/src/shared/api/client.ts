// The one HTTP client of the panel (frontend-architecture: features call typed functions, never fetch).
// It talks to /api/panel only, sends the auth cookie, adds the antiforgery header to every
// request that changes something, and turns errors into Arabic messages.

export const API_BASE = "/api/panel";
const CSRF_HEADER = "X-CSRF-TOKEN";

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fields: Record<string, string[]> | undefined;
  /** The whole JSON answer, for the few errors that carry more (the e-mail of an account not confirmed yet). */
  readonly data: Record<string, unknown> | undefined;

  constructor(status: number, message: string, code?: string, fields?: Record<string, string[]>, data?: Record<string, unknown>) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fields = fields;
    this.data = data;
  }
}

/** Arabic text for the error codes the API returns (docs/panel-api.md). */
const MESSAGES: Record<string, string> = {
  invalid_credentials: "اسم المستخدم أو كلمة السر غير صحيحة.",
  too_many_attempts: "محاولات كثيرة. انتظر قليلًا ثم حاول مرة أخرى.",
  must_change_password: "يجب تغيير كلمة السر أولًا.",
  wrong_current_password: "كلمة السر الحالية غير صحيحة.",
  weak_password: "يجب أن تتكون كلمة السر من 12 إلى 128 حرفًا.",
  same_password: "يجب أن تختلف كلمة السر الجديدة عن الحالية.",
  transition_not_allowed: "هذه الحالة غير متاحة للمهمة الآن.",
  actor_not_allowed: "غير مسموح لك بتغيير هذه الحالة.",
  username_taken: "اسم المستخدم هذا مسجّل من قبل.",
  email_taken: "هذا البريد الإلكتروني مسجّل من قبل.",
  last_manager: "يجب أن يبقى مدير واحد على الأقل مفعّلًا.",
  maintenance_only: "هذا متاح لحساب الصيانة فقط.",
  two_factor_kept: "يجب أن يبقى التحقق بخطوتين مفعّلًا على حساب الصيانة.",
  cannot_change_self: "لا يمكنك تنفيذ هذا على حسابك.",
  staff_required: "اختر الموظف المرتبط بالحساب.",
  invalid_code: "الرمز غير صحيح أو انتهت صلاحيته. اطلب رمزًا جديدًا عند الحاجة.",
  expired_ticket: "انتهى الوقت. سجّل الدخول بكلمة السر مرة أخرى.",
  email_not_verified: "لم تؤكّد بريدك الإلكتروني بعد.",
  pending_approval: "حسابك في انتظار تفعيل المدير. ستصلك رسالة بالبريد الإلكتروني فور تفعيله.",
  not_configured: "هذه الخدمة غير متاحة بعد. تواصل مع مدير المكتب.",
  not_pending: "هذا الحساب مفعّل بالفعل.",
  already_enabled: "التحقق بخطوتين مفعّل بالفعل.",
  not_enabled: "التحقق بخطوتين غير مفعّل.",
  no_setup: "ابدأ الإعداد من البداية.",
  invalid_role: "اختر الدور.",
  two_factor_required: "يجب تفعيل التحقق بخطوتين أولًا.",
  chatwoot_unavailable: "لا يستجيب Chatwoot الآن. حاول مرة أخرى بعد قليل.",
  // TASK 08: the booking
  overlapping_ranges: "توجد فترتان متداخلتان في اليوم نفسه.",
  invalid_range: "يجب أن تكون نهاية الفترة بعد بدايتها.",
  past_day: "هذا اليوم قد مضى.",
  nothing_to_undo: "لا يوجد تعديل للتراجع عنه.",
  not_confirmed: "هذا الحجز غير مؤكد الآن.",
  calendar_id: "البريد الإلكتروني للتقويم غير صحيح.",
  buffer_minutes: "الوقت بين المواعيد من 0 إلى 240 دقيقة.",
  min_notice_hours: "أقل مدة قبل الحجز من 0 إلى 168 ساعة.",
  horizon_days: "أبعد يوم من 1 إلى 180 يومًا.",
  max_per_day: "الحد الأقصى في اليوم من 1 إلى 100، أو اتركه فارغًا.",
  slot_step_minutes: "اختر الفاصل الزمني بين بدايات المواعيد.",
  offer_count: "عدد المواعيد التي يعرضها حازم من 1 إلى 9.",
  invalid: "بعض البيانات غير صحيحة.",
  // TASK 09 / 10: payments and the service orders
  invalid_amount: "هذا المبلغ غير مناسب لهذا الاختيار.",
  not_allowed: "حالة هذا الطلب لا تسمح بهذا الإجراء الآن.",
  hold_minutes: "مدة الحجز المؤقت من 15 إلى 1440 دقيقة.",
  hold_short_minutes: "مدة الموعد القريب من 15 دقيقة حتى مدة الحجز المؤقت.",
  hold_short_within_hours: "«قريب» من 0 إلى 72 ساعة.",
  hold_gap_minutes: "ينتهي الحجز المؤقت قبل الموعد بمدة من 0 إلى 240 دقيقة.",
  out_of_range: "السعر خارج النطاق المعتاد لهذه الخدمة. حدّد «استثناء» إذا كنت متأكدًا.",
  upload_failed: "تعذّر رفع الملف. حاول مرة أخرى.",
  no_file: "لم يُرفع الملف. ارفعه مرة أخرى.",
  too_large: "حجم الملف أكبر من 20 ميجابايت.",
  urf_minutes: "مدة موعد العرفي من 15 إلى 480 دقيقة.",
  office_hours: "ساعات المقابلات في المكتب: يجب أن تبدأ قبل أن تنتهي، داخل اليوم نفسه.",
  // the account's photo (migration 0036)
  avatar_type: "يجب أن تكون الصورة بصيغة JPG أو PNG أو WebP.",
  avatar_too_large: "حجم الصورة أكبر من 300 كيلوبايت. اختر صورة أصغر.",
  avatar_not_image: "هذا الملف ليس صورة صالحة. جرّب صورة أخرى.",
  // codes that showed in English («Bad Request», «Conflict») before
  antiforgery: "الصفحة قديمة. حدّث الصفحة ثم حاول مرة أخرى.",
  changed_meanwhile: "غيّر شخص آخر هذا للتو. تم تحديث الصفحة، فراجعها ثم حاول مرة أخرى.",
  has_bookings: "توجد حجوزات ستتأثر بهذا التعديل.",
  calendar_id_forbidden: "تغيير البريد الإلكتروني للتقويم متاح للمدير فقط.",
  office_forbidden: "التراجع عن هذا التعديل متاح للمدير فقط.",
  invalid_price: "يجب أن يكون السعر عددًا صحيحًا من 1 إلى 1,000,000.",
  review_range: "يجب أن يكون أقل سعر للمراجعة أقل من أعلى سعر أو مساويًا له.",
  invalid_month: "اختر شهرًا من 2024 حتى الآن.",
  invalid_phone: "الرقم غير صحيح. اكتبه بالشكل 01012345678 أو +201012345678.",
  invalid_kind: "هذا النوع غير موجود.",
  invalid_filter: "هذا الفلتر غير موجود.",
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && MESSAGES[error.code]) return MESSAGES[error.code]!;
    if (error.status === 401) return "انتهت الجلسة. سجّل الدخول مرة أخرى.";
    if (error.status === 403) return "غير مسموح لك بعرض هذه الصفحة.";
    if (error.status === 404) return "لم نعثر على المطلوب. ربما حُذف، أو ليس لديك صلاحية عرضه.";
    if (error.status === 429) return MESSAGES.too_many_attempts!;
    if (error.status >= 500) return "تعذّر تنفيذ الطلب بسبب خطأ في الخادم. حاول مرة أخرى بعد قليل.";
    // a code with no Arabic text yet: never the browser's English («Bad Request», «Conflict»)
    if (error.status === 409) return "لا يمكن تنفيذ هذا الإجراء الآن. حدّث الصفحة ثم حاول مرة أخرى.";
    return "بعض البيانات المُدخلة غير صحيحة. راجعها ثم حاول مرة أخرى.";
  }
  return "تعذّر الاتصال بالخادم. تحقّق من الاتصال ثم حاول مرة أخرى.";
}

let csrfToken: string | null = null;

/** The antiforgery token is tied to the signed-in user: forget it on login and logout. */
export function resetCsrf(): void {
  csrfToken = null;
}

async function getCsrf(): Promise<string> {
  if (csrfToken) return csrfToken;
  const res = await fetch(`${API_BASE}/auth/csrf`, { credentials: "same-origin", headers: { Accept: "application/json" } });
  if (!res.ok) throw await toError(res);
  const body = (await res.json()) as { token: string };
  csrfToken = body.token;
  return csrfToken;
}

async function toError(res: Response): Promise<ApiError> {
  let code: string | undefined;
  let fields: Record<string, string[]> | undefined;
  let data: Record<string, unknown> | undefined;
  let message = res.statusText || `HTTP ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string; title?: string; errors?: Record<string, string[]> };
    code = body.error;
    fields = body.errors;
    message = body.title ?? message;
    data = body as Record<string, unknown>;
  } catch {
    // not JSON
  }
  return new ApiError(res.status, message, code, fields, data);
}

type Method = "GET" | "POST" | "PUT" | "DELETE";

/**
 * TASK 10 (v1.14): a file straight to storage with a link the API signed (a same-origin /storage/ path the panel's
 * nginx forwards). No cookie and no CSRF token go with it: the signature is the permission.
 */
export async function putToStorage(url: string, file: Blob, contentType: string): Promise<void> {
  if (!url.startsWith("/storage/")) throw new ApiError(400, "invalid upload link", "upload_failed");
  const res = await fetch(url, { method: "PUT", headers: { "Content-Type": contentType }, body: file, credentials: "omit" });
  if (!res.ok) throw new ApiError(res.status, "upload failed", "upload_failed");
}

let earlyMe: Promise<Response> | null = null;

/** src/boot.ts: «who is signed in» asked while the rest of the panel's code still loads (idea from PR #14). */
export function askMeEarly(): void {
  earlyMe = fetch(`${API_BASE}/auth/me`, { credentials: "same-origin", headers: { Accept: "application/json" } });
  earlyMe.catch(() => undefined); // a failed early request is simply asked again
}

/** The early answer, once (useMe); null when there is none or it failed, and then useMe asks again. */
export async function takeEarlyMe(): Promise<Response | null> {
  const p = earlyMe;
  earlyMe = null;
  return p ? p.catch(() => null) : null;
}

/** A finished answer as the panel reads every answer: the JSON, nothing for an empty body, an ApiError otherwise. */
export async function read<T>(res: Response): Promise<T> {
  if (!res.ok) throw await toError(res);
  const text = res.status === 204 ? "" : await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export function api<T>(method: Method, path: string, body?: unknown): Promise<T> {
  return send<T>(method, path, body === undefined ? undefined : { type: "application/json", data: JSON.stringify(body) });
}

/** A file as the whole body, with its own Content-Type (the account's photo), never JSON. */
export function upload<T>(method: Method, path: string, file: Blob): Promise<T> {
  return send<T>(method, path, { type: file.type, data: file });
}

async function send<T>(method: Method, path: string, body?: { type: string; data: BodyInit }): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = body.type;
  if (method !== "GET") headers[CSRF_HEADER] = await getCsrf();

  let res = await fetch(`${API_BASE}${path}`, { method, headers, credentials: "same-origin", body: body?.data });

  // A token from before a login/logout is refused once: fetch a fresh one and retry.
  if (res.status === 400 && method !== "GET" && res.headers.get("X-Antiforgery-Failed") === "1") {
    resetCsrf();
    headers[CSRF_HEADER] = await getCsrf();
    res = await fetch(`${API_BASE}${path}`, { method, headers, credentials: "same-origin", body: body?.data });
  }

  // A success with no body (204, or 202 «accepted» as /auth/forgot answers) is no error: reading it as JSON
  // threw and the page said «مش قادرين نوصل للسيرفر» although the code was sent.
  return read<T>(res);
}
