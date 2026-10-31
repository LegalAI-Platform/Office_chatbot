import { useRef, useState, type ChangeEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { ApiError, errorMessage } from "../../shared/api/client";
import { avatarUrl, meKey, useLogout, useMe } from "../../shared/api/session";
import type { Me } from "../../shared/api/types";
import { label, ROLES } from "../../shared/lib/labels";
import { Avatar } from "../../shared/ui/Avatar";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card } from "../../shared/ui/ui";
import { removeAvatar, setAvatar } from "./api";
import { Notice } from "./AuthLayout";
import { PHOTO_TYPES, squareJpeg } from "./photo";
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
      <PhotoCard me={me} />
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

/** «الصورة الشخصية»: shown next to the name at the top (and later in the lists); the initials without one. */
function PhotoCard({ me }: { me: Me }) {
  const qc = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [wrongType, setWrongType] = useState(false);
  // the top bar shows the new photo at once (its address carries the new avatar_at)
  const refresh = () => qc.invalidateQueries({ queryKey: meKey });
  const save = useMutation({
    mutationFn: async (file: File) => {
      const photo = await squareJpeg(file).catch(() => {
        throw new ApiError(400, "unreadable", "avatar_not_image");
      });
      return setAvatar(photo);
    },
    onSuccess: refresh,
  });
  const remove = useMutation({ mutationFn: removeAvatar, onSuccess: refresh });

  const pick = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ""; // the same file can be picked again
    if (!file) return;
    save.reset();
    remove.reset();
    const ok = PHOTO_TYPES.includes(file.type);
    setWrongType(!ok);
    if (ok) save.mutate(file);
  };
  const failed = wrongType ? "الصورة لازم تبقى JPG أو PNG أو WebP." : save.isError ? errorMessage(save.error) : remove.isError ? errorMessage(remove.error) : null;

  return (
    <Card>
      <h2 className="text-lg">الصورة الشخصية</h2>
      <div className="flex flex-wrap items-center gap-4">
        <Avatar name={me.display_name} photo={avatarUrl(me)} className="size-24 text-2xl" />
        <div className="grid gap-2">
          <p className="text-sm text-ink-soft">بتظهر جنب اسمك فوق. أي صورة هتتقصّ مربع وتصغر لوحدها.</p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => input.current?.click()} busy={save.isPending} disabled={remove.isPending}>
              <Icon name="account" className="size-4" />
              اختيار صورة
            </Button>
            {me.avatar_at ? (
              <Button variant="ghost" onClick={() => remove.mutate()} busy={remove.isPending} disabled={save.isPending}>
                <Icon name="x" className="size-4" />
                شيل الصورة
              </Button>
            ) : null}
          </div>
          <input ref={input} type="file" accept="image/*" aria-label="ملف الصورة" className="hidden" onChange={pick} />
        </div>
      </div>
      {failed ? <Notice>{failed}</Notice> : null}
    </Card>
  );
}
