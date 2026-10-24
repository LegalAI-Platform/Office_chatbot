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
  invalid_credentials: "اسم المستخدم أو كلمة السر غلط.",
  too_many_attempts: "محاولات كتير. استنى شوية وجرّب تاني.",
  must_change_password: "لازم تغيّر كلمة السر الأول.",
  wrong_current_password: "كلمة السر الحالية غلط.",
  weak_password: "كلمة السر لازم تبقى 12 حرف على الأقل.",
  same_password: "كلمة السر الجديدة لازم تبقى مختلفة عن الحالية.",
  transition_not_allowed: "الحالة دي مش متاحة للمهمة دلوقتي.",
  actor_not_allowed: "مش مسموح لك تغيّر الحالة دي.",
  username_taken: "اسم المستخدم ده موجود قبل كده.",
  email_taken: "الإيميل ده موجود قبل كده.",
  last_manager: "لازم يفضل مدير واحد على الأقل شغال.",
  cannot_change_self: "ما ينفعش تعمل ده على حسابك.",
  staff_required: "اختار الموظف المرتبط بالحساب.",
  invalid_code: "الكود غلط أو انتهى. اطلب كود جديد لو محتاج.",
  expired_ticket: "الوقت خلص. ادخل بكلمة السر تاني.",
  email_not_verified: "لسه ما أكدتش الإيميل.",
  pending_approval: "حسابك مستني المدير يفعّله. هيوصلك إيميل أول ما يتفعّل.",
  not_configured: "الخدمة دي لسه مش متاحة. كلّم مدير المكتب.",
  not_pending: "الحساب ده متفعّل خلاص.",
  already_enabled: "التحقق بخطوتين شغال خلاص.",
  not_enabled: "التحقق بخطوتين مش شغال.",
  no_setup: "ابدأ الإعداد من الأول.",
  invalid_role: "اختار الدور.",
  two_factor_required: "لازم تشغّل التحقق بخطوتين الأول.",
  chatwoot_unavailable: "Chatwoot مش بيرد دلوقتي. جرّب تاني بعد شوية.",
  // TASK 08: the booking
  overlapping_ranges: "فيه فترتين في نفس اليوم داخلين في بعض.",
  invalid_range: "نهاية الفترة لازم تبقى بعد بدايتها.",
  past_day: "اليوم ده عدّى.",
  nothing_to_undo: "مفيش تعديل تتراجع عنه.",
  not_confirmed: "الحجز ده مش مؤكد دلوقتي.",
  calendar_id: "إيميل الكالندر مش صحيح.",
  buffer_minutes: "الوقت بين المواعيد من 0 لـ 240 دقيقة.",
  min_notice_hours: "أقل مدة قبل الحجز من 0 لـ 168 ساعة.",
  horizon_days: "أبعد يوم من 1 لـ 180 يوم.",
  max_per_day: "أقصى عدد في اليوم من 1 لـ 100، أو سيبه فاضي.",
  slot_step_minutes: "اختار كل قد إيه يبدأ ميعاد.",
  offer_count: "عدد المواعيد اللي حازم يعرضها من 1 لـ 9.",
  invalid: "في حاجة مش مظبوطة في البيانات.",
  // TASK 09 / 10: payments and the service orders
  invalid_amount: "المبلغ ده مش مناسب للاختيار ده.",
  not_allowed: "الطلب ده مش في الحالة اللي تسمح بده دلوقتي.",
  hold_minutes: "مدة الحجز المؤقت من 15 لـ 1440 دقيقة.",
  hold_short_minutes: "مدة الميعاد القريب من 15 دقيقة لحد مدة الحجز المؤقت.",
  hold_short_within_hours: "«قريب» من 0 لـ 72 ساعة.",
  hold_gap_minutes: "الحجز المؤقت يخلص قبل الميعاد بـ 0 لـ 240 دقيقة.",
  out_of_range: "السعر برا المدى المعتاد للخدمة دي. علّم «استثناء» لو متأكد.",
  upload_failed: "رفع الملف ما نجحش. جرّب تاني.",
  no_file: "الملف ما اترفعش. ارفعه تاني.",
  too_large: "الملف أكبر من 20 ميجا.",
  urf_minutes: "مدة ميعاد العرفي من 15 لـ 480 دقيقة.",
};

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && MESSAGES[error.code]) return MESSAGES[error.code]!;
    if (error.status === 401) return "انتهت الجلسة. سجّل دخول تاني.";
    if (error.status === 403) return "مش مسموح لك تشوف ده.";
    if (error.status === 404) return "مش لاقيين ده. يمكن اتمسح أو مش من حقك تشوفه.";
    if (error.status === 429) return MESSAGES.too_many_attempts!;
    if (error.status >= 500) return "حصلت مشكلة عندنا. جرّب تاني بعد شوية.";
    return error.message;
  }
  return "مش قادرين نوصل للسيرفر. اتأكد من الاتصال وجرّب تاني.";
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

export async function api<T>(method: Method, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (method !== "GET") headers[CSRF_HEADER] = await getCsrf();

  let res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    credentials: "same-origin",
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  // A token from before a login/logout is refused once: fetch a fresh one and retry.
  if (res.status === 400 && method !== "GET" && res.headers.get("X-Antiforgery-Failed") === "1") {
    resetCsrf();
    headers[CSRF_HEADER] = await getCsrf();
    res = await fetch(`${API_BASE}${path}`, {
      method,
      headers,
      credentials: "same-origin",
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  }

  if (!res.ok) throw await toError(res);
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
