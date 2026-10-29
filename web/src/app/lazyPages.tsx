// Each page is its own file, loaded the first time it is needed (idea from PR #14): the sign-in and the home screen
// arrive with the panel, the rest on the way. Once the panel is idle it fetches them all, and a hover or focus on a
// page's link fetches that page at once, so a click never waits.
import { lazy, Suspense, type ComponentType, type LazyExoticComponent } from "react";
import { Loading } from "../shared/ui/ui";

type Loader = () => Promise<ComponentType>;
const pick = <M,>(load: () => Promise<M>, name: keyof M): Loader => () => load().then((m) => m[name] as ComponentType);

const LOADERS = {
  "/tasks": pick(() => import("../features/tasks/TasksPage"), "TasksPage"),
  "/tasks/:id": pick(() => import("../features/tasks/TaskDetailPage"), "TaskDetailPage"),
  "/appointments": pick(() => import("../features/booking/BookingPage"), "BookingPage"),
  "/receipts": pick(() => import("../features/payments/PaymentsPage"), "PaymentsPage"),
  "/services": pick(() => import("../features/services/ServicesPage"), "ServicesPage"),
  "/urf": pick(() => import("../features/urf/UrfPage"), "UrfPage"),
  "/prices": pick(() => import("../features/prices/PricesPage"), "PricesPage"),
  "/chats": pick(() => import("../features/chats/ChatsPage"), "ChatsPage"),
  "/reports": pick(() => import("../features/reports/ReportsPage"), "ReportsPage"),
  "/lists": pick(() => import("../features/lists/ListsPage"), "ListsPage"),
  "/texts": pick(() => import("../features/texts/TextsPage"), "TextsPage"),
  "/users": pick(() => import("../features/users/UsersPage"), "UsersPage"),
  "/account": pick(() => import("../features/auth/AccountPage"), "AccountPage"),
  "/register": pick(() => import("../features/auth/RegisterPage"), "RegisterPage"),
  "/verify-email": pick(() => import("../features/auth/VerifyEmailPage"), "VerifyEmailPage"),
  "/forgot-password": pick(() => import("../features/auth/ForgotPasswordPage"), "ForgotPasswordPage"),
  "/change-password": pick(() => import("../features/auth/ChangePasswordPage"), "ChangePasswordPage"),
  "/setup-2fa": pick(() => import("../features/auth/SetupTwoFactorPage"), "SetupTwoFactorPage"),
} satisfies Record<string, Loader>;

export type LazyPath = keyof typeof LOADERS;

// One promise per page: a prefetch and the real render share it.
const started = new Map<LazyPath, Promise<ComponentType>>();
const load = (path: LazyPath) => {
  let p = started.get(path);
  if (!p) {
    p = LOADERS[path]();
    p.catch(() => started.delete(path)); // a failed download (offline for a moment) is tried again on the next visit
    started.set(path, p);
  }
  return p;
};

const components = new Map<LazyPath, LazyExoticComponent<ComponentType>>();

/** The page at `path`, rendered once its file has arrived (the panel's loader until then). */
export function LazyPage({ path }: { path: LazyPath }) {
  let C = components.get(path);
  if (!C) {
    C = lazy(() => load(path).then((c) => ({ default: c })));
    components.set(path, C);
  }
  return (
    <Suspense fallback={<div className="p-1"><Loading /></div>}>
      <C />
    </Suspense>
  );
}

/** The page behind a link (on hover or focus): its file, ahead of the click. */
export function prefetch(to: string) {
  const path = (to.split("?")[0] ?? to) as LazyPath;
  if (!(path in LOADERS)) return;
  void load(path).catch(() => undefined);
}

/** Once the browser is idle, every page the person can open, so later clicks never wait. */
export function prefetchAll(paths: string[]) {
  const run = () => paths.forEach((p) => prefetch(p));
  if ("requestIdleCallback" in window) window.requestIdleCallback(run, { timeout: 4000 });
  else setTimeout(run, 2000);
}
