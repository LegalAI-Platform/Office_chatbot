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
    <AuthLayout title="شغّل التحقق بخطوتين"
      subtitle="حسابك بيقدر يدير كل الحسابات، فلازم يبقى محمي بالموبايل كمان. خطوة واحدة وبعدها تكمّل.">
      <TwoFactorCard />
      <Button variant="primary" disabled={!done} onClick={() => navigate("/", { replace: true })}>كمّل للوحة</Button>
      <button type="button" className="text-sm font-semibold text-accent-ink hover:underline"
        onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
        خروج
      </button>
    </AuthLayout>
  );
}
