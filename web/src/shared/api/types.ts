// The /api/panel contract (snake_case, like the rest of the API). docs/panel-api.md.

export type Role = "manager" | "lawyer" | "staff";

export type Permission =
  | "tasks.view_own"
  | "tasks.view_all"
  | "tasks.update"
  | "tasks.assign"
  | "users.manage"
  | "account.self"
  | "lists.manage"
  | "texts.manage"
  | "reports.view"
  | "conversations.unmute"
  | "schedule.manage"
  | "office.manage"
  | "payments.verify"
  | "payments.refund"
  | "services.manage"
  | "services.price"
  | "urf.manage"
  | "urf.review"
  | "prices.manage"
  | "testing.forget";

export interface Me {
  id: string;
  username: string;
  display_name: string;
  email: string;
  role: Role;
  permissions: Permission[];
  must_change_password: boolean;
  /** The authenticator app is on (v1.9). */
  two_factor: boolean;
  /** A manager without the authenticator: nothing else works until it is on. */
  two_factor_required: boolean;
  /** When the account's photo was set (its cache key), null without one: /users/{id}/avatar?v=… */
  avatar_at: string | null;
}

/** The right password of an account with the authenticator on: the code comes next. */
export interface SecondStep {
  two_factor: true;
  ticket: string;
}

export interface TwoFactorStatus {
  enabled: boolean;
  recovery_codes_left: number;
  /** PANEL_SECRET_KEY is set on the server. */
  available: boolean;
}

export type TaskState = "open" | "in_progress" | "done" | "overdue" | "cancelled";

export interface StaffRef {
  id: string;
  name: string;
}

export interface TaskListItem {
  id: string;
  type: string;
  state: TaskState;
  due_at: string;
  created_at: string;
  staff?: StaffRef;
  client_name?: string;
  phone_last3: string;
  client_type?: string;
  matter_type?: string;
}

export interface TaskPage {
  items: TaskListItem[];
  total: number;
  page: number;
  page_size: number;
}

export interface ClientWord {
  type: string;
  text?: string;
  caption?: string;
  transcript?: string;
  media_status?: string;
  sent_at?: string;
}

export interface TaskDetail {
  id: string;
  type: string;
  state: TaskState;
  due_at: string;
  created_at: string;
  staff?: StaffRef;
  client: {
    name?: string;
    phone: string;
    client_type?: string;
    source?: string;
    conversation_state?: string;
  };
  matter_type?: string;
  matter_subtype?: string;
  client_words: ClientWord[];
  can: { start: boolean; done: boolean; cancel: boolean; assign: boolean };
}

export interface PanelUser {
  id: string;
  username: string;
  display_name: string;
  email: string;
  /** Null while a registered account waits for the manager (v1.9). */
  role: Role | null;
  staff?: StaffRef;
  active: boolean;
  locked: boolean;
  must_change_password: boolean;
  last_login_at?: string;
  pending: boolean;
  email_verified: boolean;
  two_factor: boolean;
  created_at: string;
}

export interface NewUserResult {
  user: PanelUser;
  temporary_password: string;
}

export interface DashboardTask {
  id: string;
  type: string;
  state: TaskState;
  due_at: string;
  created_at: string;
  client_name?: string;
  staff_name?: string;
  late: boolean;
}

export interface Dashboard {
  urgent: number;
  overdue: number;
  unassigned: number;
  active: number;
  /** the last 7 Cairo days, oldest first */
  urgent_by_day: number[];
  overdue_by_day: number[];
  recent: DashboardTask[];
  priority: DashboardTask[];
}

// ---------- TASK 07c ----------

export type AlertKind = "task_overdue" | "task_urgent" | "task_human" | "task_rating" | "system";

/** TASK 13: a system alert (the managers only) has no task: `system_what` says what failed. */
export interface PanelAlert {
  id: number;
  kind: AlertKind;
  task_id?: string;
  task_type?: string;
  client_name?: string;
  due_at?: string;
  created_at: string;
  read: boolean;
  system_what?: "backup" | "healthcheck" | "drill" | "log_scan" | "n8n";
}

export interface AlertList {
  unread: number;
  items: PanelAlert[];
}

export type ListKind = "existing_client" | "lawyer_direct" | "blocked";

export interface ListEntry {
  id: string;
  kind: ListKind;
  wa_id: string;
  name?: string;
  staff_name?: string;
  note?: string;
  imported_at: string;
}

export interface MutedConversation {
  wa_id: string;
  name?: string;
  since: string;
}

export interface ApprovedText {
  key: string;
  version: number;
  text: string;
  max_chars: number;
  source: string;
  updated_at: string;
  updated_by?: string;
  versions: number;
}

export interface TextVersion {
  version: number;
  text: string;
  active: boolean;
  source: string;
  updated_at: string;
  updated_by?: string;
}

export interface ReportRow {
  key: string;
  count: number;
}

/** «الشهور»: one month's money that came, and its paid bookings. */
export interface TrendRow {
  month: string;
  received: number;
  booked: number;
}

/** One service of the month: paid bookings (or priced orders), the ones done, what came, the price (null: priced one by one). */
export interface ServiceReportRow {
  key: string;
  booked: number;
  completed: number;
  received: number;
  price: number | null;
}

export interface MonthReport {
  month: string;
  tasks: number;
  done: number;
  done_on_time: number;
  late: number;
  by_type: ReportRow[];
  by_staff: { staff: string; assigned: number; done: number; on_time: number }[];
  new_clients: number;
  by_source: ReportRow[];
  by_matter: ReportRow[];
  by_client_type: ReportRow[];
  by_booking: ReportRow[];
  received: number;
  missing: number;
  refunded: number;
  /** v1.31 (B-26) «تقييمات العملاء»: the ratings sent in the month and their answers. */
  ratings: RatingReport;
  /** «حسب الخدمة»: each service's bookings (or orders), the ones done, its money and its price. */
  by_service?: ServiceReportRow[];
  /** v1.39 (TASK 16) «محادثات محتاجة مراجعة»: the month's conversations the system flagged. */
  review?: ReviewRow[];
  /** v1.53 (B-34) «ردود حازم الحرة»: each free answer of the month, sent or refused and why. */
  free_answers?: FreeAnswerRow[];
  /** v1.56: every reply Hazem wrote this month, how many fell back to the approved words, and why. */
  written?: WrittenSummary;
  /** v1.58: what the AI cost this month. */
  ai_cost?: AiCostSummary;
}

/** v1.58 (the owner: «المحادثة صرفت قد إيه من الكريدت؟»): the month's AI spend in USD. */
export interface AiCostSummary {
  total_usd: number;
  calls: number;
  conversations: number;
  input_tokens: number;
  output_tokens: number;
  /** calls of a model with no known price: their tokens count, their cost does not */
  unpriced: number;
}

/** v1.58: one client's conversation in the month. */
export interface AiCostConversation {
  person_id: string;
  client: string | null;
  turns: number;
  calls: number;
  input_tokens: number;
  output_tokens: number;
  cost_usd: number;
  last_at: string;
  unpriced: number;
}

export interface AiCostReport {
  month: string;
  summary: AiCostSummary;
  conversations: AiCostConversation[];
}

/** v1.58: one AI call of a message: what it was for (classify, write, review), its tokens and cost. */
export interface AiCostCall {
  kind: "classify" | "write" | "review";
  model: string;
  input_tokens: number;
  cache_read_tokens: number;
  cache_write_tokens: number;
  output_tokens: number;
  cost_usd: number | null;
}

/** v1.58: one message of a conversation (never its words). */
export interface AiCostTurn {
  at: string;
  cost_usd: number;
  input_tokens: number;
  output_tokens: number;
  calls: AiCostCall[];
}

export interface WrittenSummary {
  total: number;
  fallbacks: number;
  /** written a second time after a refusal */
  retried: number;
  by_reason: ReportRow[];
}

/** v1.53: one free answer (Hazem's words only, never the client's). */
export interface FreeAnswerRow {
  /** the approved text it replaces («free_answer» when no text fitted) */
  purpose: string;
  at: string;
  client: string | null;
  /** what went; null when the approved words went instead */
  text: string | null;
  /** «passed» or why it was refused */
  guard: string;
}

/** v1.39: why a conversation is flagged (keys and reasons only, never the messages). */
export interface ReviewRow {
  at: string;
  client: string | null;
  reason: "repeated" | "unclear_twice" | "asked_status" | "offer_after_payment" | "left_after_price";
}

export interface RatingReport {
  sent: number;
  /** with a score (2 to 5); «ليس الآن» is no score */
  answered: number;
  average?: number | null; // the API leaves it out when there is none
  by_service: { key: string; count: number; average: number }[];
  low: { at: string; service: string; client_name: string | null }[];
}
