import { setTheme, useTheme, type Theme } from "../lib/theme";

const LOOKS: [Theme, string][] = [["light", "فاتح"], ["dark", "داكن"]];

/**
 * «فاتح / غامق»: two equal buttons with a sliding mark; the new look spreads from the pressed one (theme.ts).
 * `onDark`: the black side bar's glass instead (the sign-in pages' black band on a phone), the same in both looks.
 */
export function ThemePicker({ labelledBy, label, className = "", onDark = false }: {
  labelledBy?: string; label?: string; className?: string; onDark?: boolean;
}) {
  const theme = useTheme();
  const at = LOOKS.findIndex(([v]) => v === theme);
  return (
    <div role="radiogroup" aria-labelledby={labelledBy} aria-label={labelledBy ? undefined : label}
      className={`relative grid grid-cols-2 rounded-full p-1 ${onDark
        ? "border border-white/10 bg-white/[0.06] shadow-[inset_0_1px_0_rgb(255_255_255/0.08)] backdrop-blur-md"
        : "bg-muted"} ${className}`}>
      <span aria-hidden className={`absolute inset-y-1 w-[calc(50%-4px)] rounded-full transition-[inset-inline-start] duration-300 ease-out ${onDark
        ? "border border-white/25 bg-white/15 shadow-[inset_0_1px_0_rgb(255_255_255/0.3)]"
        : "bg-card shadow-sm"}`}
        style={{ insetInlineStart: at === 1 ? "50%" : "4px" }} />
      {LOOKS.map(([v, text]) => (
        <button key={v} type="button" role="radio" aria-checked={v === theme}
          onClick={(e) => setTheme(v, e.currentTarget)}
          className={`relative z-10 min-h-9 rounded-full text-sm ${onDark ? "px-3" : "px-4"} ${v === theme
            ? `font-semibold ${onDark ? "text-white" : "text-ink"}`
            : onDark ? "text-side-soft hover:text-side-ink" : "text-ink-soft hover:text-ink"}`}>
          {text}
        </button>
      ))}
    </div>
  );
}
