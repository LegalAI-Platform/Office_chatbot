import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createBrowserRouter, Navigate, Outlet, RouterProvider, useLocation } from "react-router";
import { errorMessage } from "../shared/api/client";
import { can, useMe } from "../shared/api/session";
import type { Permission } from "../shared/api/types";
import { ErrorState, Loading } from "../shared/ui/ui";
import { AccountPage } from "../features/auth/AccountPage";
import { ChangePasswordPage } from "../features/auth/ChangePasswordPage";
import { ForgotPasswordPage } from "../features/auth/ForgotPasswordPage";
import { LoginPage } from "../features/auth/LoginPage";
import { RegisterPage } from "../features/auth/RegisterPage";
import { SetupTwoFactorPage } from "../features/auth/SetupTwoFactorPage";
import { VerifyEmailPage } from "../features/auth/VerifyEmailPage";
import { TaskDetailPage } from "../features/tasks/TaskDetailPage";
import { TasksPage } from "../features/tasks/TasksPage";
import { UsersPage } from "../features/users/UsersPage";
import { ChatsPage } from "../features/chats/ChatsPage";
import { ListsPage } from "../features/lists/ListsPage";
import { ReportsPage } from "../features/reports/ReportsPage";
import { TextsPage } from "../features/texts/TextsPage";
import { HomePage } from "../features/home/HomePage";
import { BookingPage } from "../features/booking/BookingPage";
import { Layout } from "./Layout";
import { ComingSoon, SettingsPage } from "./Pages";

export function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: 1, refetchOnWindowFocus: true },
    },
  });
}

/** Signed in, and a temporary password already changed; otherwise to the right page. */
export function RequireAuth() {
  const me = useMe();
  const location = useLocation();
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
  { path: "/register", element: <RegisterPage /> },
  { path: "/verify-email", element: <VerifyEmailPage /> },
  { path: "/forgot-password", element: <ForgotPasswordPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: "/change-password", element: <ChangePasswordPage /> },
      { path: "/setup-2fa", element: <SetupTwoFactorPage /> },
      {
        element: <Layout />,
        children: [
          { path: "/", element: <HomePage /> },
          { path: "/tasks", element: <TasksPage /> },
          { path: "/tasks/:id", element: <TaskDetailPage /> },
          { path: "/account", element: <AccountPage /> },
          { path: "/settings", element: <SettingsPage /> },
          { path: "/clients", element: <ComingSoon title="العملاء / الشركات" icon="clients" what="ملف لكل عميل: بياناته، قضاياه، مواعيده ومستنداته في مكان واحد." when="بعد ربط المحادثات (TASK 07b)" /> },
          {
            element: <RequirePermission permission="schedule.manage" />,
            children: [{ path: "/appointments", element: <BookingPage /> }],
          },
          { path: "/chats", element: <ChatsPage /> },
          { path: "/documents", element: <ComingSoon title="المستندات" icon="document" what="العقود والصور والمستندات اللي العملاء بعتوها، مرتبة على كل عميل." when="بعد المحادثات" /> },
          {
            element: <RequirePermission permission="reports.view" />,
            children: [{ path: "/reports", element: <ReportsPage /> }],
          },
          {
            element: <RequirePermission permission="lists.manage" />,
            children: [{ path: "/lists", element: <ListsPage /> }],
          },
          {
            element: <RequirePermission permission="texts.manage" />,
            children: [{ path: "/texts", element: <TextsPage /> }],
          },
          { path: "/receipts", element: <ComingSoon title="الإيصالات" icon="receipt" what="إيصالات التحويل اللي العملاء بعتوها، تتأكد منها بضغطة." when="مع الدفع" /> },
          {
            element: <RequirePermission permission="users.manage" />,
            children: [{ path: "/users", element: <UsersPage /> }],
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
