// Sign-up, e-mail codes, a forgotten password and the authenticator (DECISIONS v1.9 T-23), and the account's photo.
import { api, upload } from "../../shared/api/client";
import type { TwoFactorStatus } from "../../shared/api/types";

export const signupAvailable = () => api<{ available: boolean }>("GET", "/auth/signup");

export const register = (body: { username: string; display_name: string; email: string; password: string }) =>
  api<{ mail_sent: boolean }>("POST", "/auth/register", body);

export const resendCode = (email: string) => api<void>("POST", "/auth/register/resend", { email });

export const verifyEmail = (email: string, code: string) => api<void>("POST", "/auth/register/verify", { email, code });

export const forgotPassword = (login: string) => api<void>("POST", "/auth/forgot", { login });

export const resetPassword = (login: string, code: string, new_password: string) =>
  api<void>("POST", "/auth/reset", { login, code, new_password });

export const twoFactorKey = ["two-factor"] as const;

export const twoFactorStatus = () => api<TwoFactorStatus>("GET", "/auth/2fa");

export const twoFactorSetup = () => api<{ secret: string; uri: string }>("POST", "/auth/2fa/setup");

export const twoFactorEnable = (code: string) => api<{ recovery_codes: string[] }>("POST", "/auth/2fa/enable", { code });

export const twoFactorDisable = (password: string, code: string) => api<void>("POST", "/auth/2fa/disable", { password, code });

export const newRecoveryCodes = (code: string) => api<{ recovery_codes: string[] }>("POST", "/auth/2fa/recovery-codes", { code });

/** The body is the photo itself (squareJpeg); /auth/me then carries its new avatar_at. */
export const setAvatar = (photo: Blob) => upload<{ avatar_at: string }>("PUT", "/account/avatar", photo);

export const removeAvatar = () => api<void>("DELETE", "/account/avatar");
