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
  | "conversations.unmute";

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

export type AlertKind = "task_overdue" | "task_urgent" | "task_human";

export interface PanelAlert {
  id: number;
  kind: AlertKind;
  task_id: string;
  task_type: string;
  client_name?: string;
  due_at: string;
  created_at: string;
  read: boolean;
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
}
