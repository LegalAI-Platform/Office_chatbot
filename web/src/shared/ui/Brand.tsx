import { useTheme } from "../lib/theme";
import logoDark from "./brand/logo-dark.webp";
import logoLight from "./brand/logo-light.webp";

/** The firm's own emblem (its gold seal), the cut drawn for a dark ground on the navy side bar. */
export function FirmLogo({ className = "size-12", onDark = false }: { className?: string; onDark?: boolean }) {
  const theme = useTheme();
  return <img src={onDark || theme === "dark" ? logoDark : logoLight} alt="" aria-hidden className={`${className} shrink-0 object-contain`} draggable={false} />;
}

/** The firm's emblem with its name, for the side bar and the sign-in page. */
export function Wordmark({ onDark = false }: { onDark?: boolean }) {
  return (
    <span className="flex min-w-0 items-center gap-3">
      <FirmLogo className="size-12" onDark={onDark} />
      <span className="grid min-w-0 leading-tight">
        <span className={`display truncate text-[15px] font-semibold ${onDark ? "text-white" : "text-ink"}`}>مكتب سعد فتحي سعد</span>
        <span className={`truncate text-[12px] ${onDark ? "text-side-soft" : "text-ink-soft"}`}>للمحاماة والاستشارات القانونية</span>
      </span>
    </span>
  );
}
