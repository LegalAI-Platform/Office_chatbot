import { useNavigate } from "react-router";
import { useLogout, useMe } from "../../shared/api/session";
import { Button } from "../../shared/ui/ui";
import { AuthLayout } from "./AuthLayout";
import { TwoFactorCard } from "./TwoFactorCard";

/** A manager must turn the authenticator on before anything else (v1.9 T-23). */
export function SetupTwoFactorPage() {
  const { data: me } = useMe();
  const navigate = useNavigate();
  const logout = useLogout();
  const done = me ? !me.two_factor_required : false;

  return (
    <AuthLayout title="تفعيل التحقق بخطوتين"
      subtitle="يستطيع حسابك إدارة كل الحسابات، لذا يجب حمايته بالهاتف أيضًا. خطوة واحدة ثم تتابع.">
      <TwoFactorCard />
      <Button variant="primary" disabled={!done} onClick={() => navigate("/", { replace: true })}>المتابعة إلى اللوحة</Button>
      <button type="button" className="text-sm font-semibold text-gold-ink hover:underline"
        onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
        خروج
      </button>
    </AuthLayout>
  );
}
