import { useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { ApiError, errorMessage } from "../../shared/api/client";
import { avatarUrl, meKey, useLogout, useMe } from "../../shared/api/session";
import type { Me } from "../../shared/api/types";
import { label, ROLES } from "../../shared/lib/labels";
import { Avatar } from "../../shared/ui/Avatar";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Button } from "../../shared/ui/ui";
import { removeAvatar, setAvatar } from "./api";
import { Notice } from "./AuthLayout";
import { PHOTO_TYPES, squareJpeg } from "./photo";
import { TwoFactorCard } from "./TwoFactorCard";

/**
 * «حسابي» as «liquid glass» (the owner: modern, clearly worked on): soft gold light behind see-through cards. The
 * header holds the photo (with its camera button), the name and the role; then the account's details, the photo and
 * the sign-in's safety.
 */
export function AccountPage() {
  const { data: me } = useMe();
  const logout = useLogout();
  const navigate = useNavigate();
  const photo = usePhoto();
  if (!me) return null;

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[34rem] overflow-hidden">
        <span className="absolute -top-20 start-[4%] size-[26rem] rounded-full bg-gold/40 blur-[90px]" />
        <span className="absolute top-10 end-[2%] size-[22rem] rounded-full bg-[#f3cf8a]/60 blur-[90px]" />
        <span className="absolute top-72 start-[38%] size-[18rem] rounded-full bg-blue/15 blur-[90px]" />
      </div>

      <ProfileHeader me={me} photo={photo} onPassword={() => navigate("/change-password")}
        onLogout={async () => { await logout(); navigate("/login", { replace: true }); }} />

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <GlassCard icon="account" title="بيانات الحساب" hint="بياناتك زي ما هي متسجلة في المكتب. لتغيير الاسم أو الإيميل كلّم مدير المكتب.">
          <dl className="grid gap-2">
            <Detail icon="account" term="الاسم">{me.display_name}</Detail>
            <Detail icon="key" term="اسم المستخدم" ltr>{me.username}</Detail>
            <Detail icon="mail" term="الإيميل" ltr>{me.email}</Detail>
            <Detail icon="shield" term="الدور">{label(ROLES, me.role)}</Detail>
          </dl>
        </GlassCard>
        <PhotoCard me={me} photo={photo} />
      </div>

      <div className="liquid-kids grid">
        <TwoFactorCard />
      </div>
    </div>
  );
}

/** The header: the photo with its camera button, the name, the role and the username, and the two account actions. */
function ProfileHeader({ me, photo, onPassword, onLogout }: { me: Me; photo: Photo; onPassword: () => void; onLogout: () => void }) {
  return (
    <header className="liquid relative overflow-hidden rounded-[28px] p-5 sm:p-7">
      {/* inside the glass: a warm sheen from the photo's side */}
      <span aria-hidden className="absolute -top-24 -start-24 size-72 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
      {/* a thin gold line along the top, the glass's lit edge */}
      <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-r from-transparent via-gold to-transparent opacity-70" />
      <div className="relative flex flex-wrap items-center gap-5 sm:gap-7">
        <PhotoPicker me={me} photo={photo} />
        <div className="grid min-w-0 flex-1 gap-2">
          <p className="text-[13px] font-semibold text-gold-ink">حسابي</p>
          <h1 className="truncate text-[26px] leading-tight font-bold sm:text-[32px]">{me.display_name}</h1>
          <div className="flex flex-wrap items-center gap-2">
            <span className="inline-flex items-center gap-1.5 rounded-full border border-gold-line bg-gold-tint px-3 py-1 text-[12.5px] font-semibold text-gold-ink">
              <Icon name="shield" className="size-3.5" />{label(ROLES, me.role)}
            </span>
            <span className="rounded-full border border-line bg-card/60 px-3 py-1 text-[12.5px] text-ink-soft" dir="ltr">@{me.username}</span>
            <span className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[12.5px] font-semibold ${me.two_factor ? "bg-green-bg text-green-ink" : "bg-card-2 text-ink-soft"}`}>
              <span className={`size-1.5 rounded-full ${me.two_factor ? "bg-green" : "bg-ink-soft/60"}`} aria-hidden />
              {me.two_factor ? "التحقق بخطوتين شغّال" : "التحقق بخطوتين مقفول"}
            </span>
          </div>
        </div>
        <div className="flex w-full flex-wrap gap-2 sm:w-auto sm:flex-col">
          <Button onClick={onPassword} className="justify-center rounded-full">
            <Icon name="key" className="size-4" />
            تغيير كلمة السر
          </Button>
          <Button variant="danger" onClick={onLogout} className="justify-center rounded-full">
            <Icon name="logout" className="size-4" />
            خروج
          </Button>
        </div>
      </div>
    </header>
  );
}

/** The see-through card of this page, with an icon tile and a title. */
function GlassCard({ icon, title, hint, children }: { icon: IconName; title: string; hint?: string; children: ReactNode }) {
  return (
    <section className="liquid grid content-start gap-4 rounded-[22px] p-5">
      <div className="flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
          <Icon name={icon} className="size-5" />
        </span>
        <div className="grid gap-0.5">
          <h2 className="text-[17px] font-bold">{title}</h2>
          {hint ? <p className="text-[12.5px] text-ink-soft">{hint}</p> : null}
        </div>
      </div>
      {children}
    </section>
  );
}

function Detail({ icon, term, ltr = false, children }: { icon: IconName; term: string; ltr?: boolean; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-2xl border border-line/70 bg-card/50 px-3.5 py-2.5">
      <Icon name={icon} className="size-4 shrink-0 text-ink-soft" />
      <dt className="w-28 shrink-0 text-[12.5px] text-ink-soft">{term}</dt>
      <dd className="min-w-0 flex-1 truncate text-[14px] font-semibold" dir={ltr ? "ltr" : undefined} style={ltr ? { textAlign: "right" } : undefined}>
        {children}
      </dd>
    </div>
  );
}

/** The photo's upload and removal, shared by the header's camera button and the photo card. */
function usePhoto() {
  const qc = useQueryClient();
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
  return { save, remove, pick, failed };
}

type Photo = ReturnType<typeof usePhoto>;

/** The big photo in the header, with a round camera button to change it. */
function PhotoPicker({ me, photo: { save, pick } }: { me: Me; photo: Photo }) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <div className="relative shrink-0">
      <span aria-hidden className="absolute -inset-1.5 rounded-full bg-gradient-to-br from-gold/60 via-gold-tint to-transparent blur-[2px]" />
      <Avatar name={me.display_name} photo={avatarUrl(me)} className="relative size-24 text-2xl ring-4 ring-card sm:size-28 sm:text-3xl" />
      <button type="button" onClick={() => input.current?.click()} disabled={save.isPending} aria-label="غيّر الصورة"
        className="btn-gold absolute -bottom-1 -end-1 grid size-9 place-items-center rounded-full border-2 border-card shadow-md transition hover:scale-105 disabled:opacity-60">
        <Icon name="camera" className="size-4" />
      </button>
      <input ref={input} type="file" accept="image/*" aria-label="ملف الصورة من الصورة الكبيرة" className="hidden" onChange={pick} />
    </div>
  );
}

/** «الصورة الشخصية»: shown next to the name at the top (and later in the lists); the initials without one. */
function PhotoCard({ me, photo: { save, remove, pick, failed } }: { me: Me; photo: Photo }) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <GlassCard icon="camera" title="الصورة الشخصية" hint="بتظهر جنب اسمك فوق. أي صورة هتتقصّ مربع وتصغر لوحدها.">
      <div className="flex flex-wrap items-center gap-4 rounded-2xl border border-dashed border-gold-line/70 bg-card/40 p-4">
        <Avatar name={me.display_name} photo={avatarUrl(me)} className="size-16 text-lg" />
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => input.current?.click()} busy={save.isPending} disabled={remove.isPending} className="rounded-full">
            <Icon name="account" className="size-4" />
            اختيار صورة
          </Button>
          {me.avatar_at ? (
            <Button variant="ghost" onClick={() => remove.mutate()} busy={remove.isPending} disabled={save.isPending} className="rounded-full">
              <Icon name="x" className="size-4" />
              شيل الصورة
            </Button>
          ) : null}
        </div>
        <input ref={input} type="file" accept="image/*" aria-label="ملف الصورة" className="hidden" onChange={pick} />
      </div>
      <p className="text-[12px] text-ink-soft">JPG أو PNG أو WebP. الصورة بتتحفظ صغيرة (256 × 256).</p>
      {failed ? <Notice>{failed}</Notice> : null}
    </GlassCard>
  );
}
