import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createBrowserRouter, Navigate, Outlet, RouterProvider, useLocation, useNavigate } from "react-router";
import { errorMessage } from "../shared/api/client";
import { can, useIdleSignOut, useMe } from "../shared/api/session";
import type { Permission } from "../shared/api/types";
import { ErrorState, Loading } from "../shared/ui/ui";
import { LoginPage } from "../features/auth/LoginPage";
import { HomePage } from "../features/home/HomePage";
import { LazyPage } from "./lazyPages";
import { Layout } from "./Layout";
import { ComingSoon, SettingsPage } from "./Pages";

export function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      // Data younger than 30 seconds is shown at once when a page opens again (a change refetches it anyway).
      queries: { retry: 1, refetchOnWindowFocus: true, staleTime: 30_000 },
    },
  });
}

/** Signed in, and a temporary password already changed; otherwise to the right page. */
export function RequireAuth() {
  const me = useMe();
  const location = useLocation();
  const navigate = useNavigate();
  useIdleSignOut(!!me.data, () =>
    navigate("/login", { replace: true, state: { notice: "خرجت أوتوماتيك عشان مفيش استخدام بقاله ساعة. ادخل تاني." } }));
  if (me.isPending) return <div className="p-6"><Loading /></div>;
  if (me.isError) return <div className="p-6"><ErrorState message={errorMessage(me.error)} onRetry={() => void me.refetch()} /></div>;
  if (!me.data) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (me.data.must_change_password && location.pathname !== "/change-password") return <Navigate to="/change-password" replace />;
  if (!me.data.must_change_password && me.data.two_factor_required && location.pathname !== "/setup-2fa") {
    return <Navigate to="/setup-2fa" replace />;
  }
  return <Outlet />;
}

/** The page is hidden from people without the permission; the API refuses them anyway. */
export function RequirePermission({ permission }: { permission: Permission }) {
  const { data: me } = useMe();
  return can(me, permission) ? <Outlet /> : <Navigate to="/" replace />;
}

export const routes = [
  { path: "/login", element: <LoginPage /> },
  // v1.9: anyone may ask for an account (the manager approves it), and a forgotten password by e-mail.
  { path: "/register", element: <LazyPage path="/register" /> },
  { path: "/verify-email", element: <LazyPage path="/verify-email" /> },
  { path: "/forgot-password", element: <LazyPage path="/forgot-password" /> },
  {
    element: <RequireAuth />,
    children: [
      { path: "/change-password", element: <LazyPage path="/change-password" /> },
      { path: "/setup-2fa", element: <LazyPage path="/setup-2fa" /> },
      {
        element: <Layout />,
        children: [
          { path: "/", element: <HomePage /> },
          { path: "/tasks", element: <LazyPage path="/tasks" /> },
          { path: "/tasks/:id", element: <LazyPage path="/tasks/:id" /> },
          { path: "/account", element: <LazyPage path="/account" /> },
          { path: "/settings", element: <SettingsPage /> },
          { path: "/clients", element: <ComingSoon title="العملاء / الشركات" icon="clients" what="ملف لكل عميل: بياناته، قضاياه، مواعيده ومستنداته في مكان واحد." /> },
          {
            element: <RequirePermission permission="schedule.manage" />,
            children: [{ path: "/appointments", element: <LazyPage path="/appointments" /> }],
          },
          { path: "/chats", element: <LazyPage path="/chats" /> },
          { path: "/documents", element: <ComingSoon title="المستندات" icon="document" what="العقود والصور والمستندات اللي العملاء بعتوها، مرتبة على كل عميل." /> },
          {
            element: <RequirePermission permission="reports.view" />,
            children: [{ path: "/reports", element: <LazyPage path="/reports" /> }],
          },
          {
            element: <RequirePermission permission="lists.manage" />,
            children: [{ path: "/lists", element: <LazyPage path="/lists" /> }],
          },
          {
            element: <RequirePermission permission="texts.manage" />,
            children: [{ path: "/texts", element: <LazyPage path="/texts" /> }],
          },
          {
            element: <RequirePermission permission="schedule.manage" />,
            children: [{ path: "/receipts", element: <LazyPage path="/receipts" /> }],
          },
          {
            element: <RequirePermission permission="services.manage" />,
            children: [{ path: "/services", element: <LazyPage path="/services" /> }],
          },
          {
            element: <RequirePermission permission="urf.manage" />,
            children: [{ path: "/urf", element: <LazyPage path="/urf" /> }],
          },
          {
            element: <RequirePermission permission="users.manage" />,
            children: [{ path: "/users", element: <LazyPage path="/users" /> }],
          },
        ],
      },
    ],
  },
  { path: "*", element: <Navigate to="/" replace /> },
];

const router = createBrowserRouter(routes);
const queryClient = makeQueryClient();

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}
