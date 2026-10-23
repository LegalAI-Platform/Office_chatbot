import { useNavigate } from "react-router";
import { useLogout, useMe } from "../../shared/api/session";
import { label, ROLES } from "../../shared/lib/labels";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card } from "../../shared/ui/ui";
import { TwoFactorCard } from "./TwoFactorCard";

export function AccountPage() {
  const { data: me } = useMe();
  const logout = useLogout();
  const navigate = useNavigate();
  if (!me) return null;

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">حسابي</h1>
      <Card>
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
          <dt className="text-ink-soft">الاسم</dt>
          <dd className="font-medium">{me.display_name}</dd>
          <dt className="text-ink-soft">اسم المستخدم</dt>
          <dd className="font-medium" dir="ltr" style={{ textAlign: "right" }}>{me.username}</dd>
          <dt className="text-ink-soft">الإيميل</dt>
          <dd className="font-medium break-all" dir="ltr" style={{ textAlign: "right" }}>{me.email}</dd>
          <dt className="text-ink-soft">الدور</dt>
          <dd className="font-medium">{label(ROLES, me.role)}</dd>
        </dl>
      </Card>
      <TwoFactorCard />
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => navigate("/change-password")}>
          <Icon name="key" className="size-4" />
          تغيير كلمة السر
        </Button>
        <Button variant="danger" onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
          <Icon name="logout" className="size-4" />
          خروج
        </Button>
      </div>
    </div>
  );
}
