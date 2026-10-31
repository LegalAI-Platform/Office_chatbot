import { setTheme, useTheme, type Theme } from "../lib/theme";

const LOOKS: [Theme, string][] = [["light", "فاتح"], ["dark", "غامق"]];

/** «فاتح / غامق»: two equal buttons with a sliding mark; the new look spreads from the pressed one (theme.ts). */
export function ThemePicker({ labelledBy, label, className = "" }: { labelledBy?: string; label?: string; className?: string }) {
  const theme = useTheme();
  const at = LOOKS.findIndex(([v]) => v === theme);
  return (
    <div role="radiogroup" aria-labelledby={labelledBy} aria-label={labelledBy ? undefined : label}
      className={`relative grid grid-cols-2 rounded-full bg-muted p-1 ${className}`}>
      <span aria-hidden className="absolute inset-y-1 w-[calc(50%-4px)] rounded-full bg-card shadow-sm transition-[inset-inline-start] duration-300 ease-out"
        style={{ insetInlineStart: at === 1 ? "50%" : "4px" }} />
      {LOOKS.map(([v, text]) => (
        <button key={v} type="button" role="radio" aria-checked={v === theme}
          onClick={(e) => setTheme(v, e.currentTarget)}
          className={`relative z-10 min-h-9 rounded-full px-4 text-sm ${v === theme ? "font-semibold text-ink" : "text-ink-soft hover:text-ink"}`}>
          {text}
        </button>
      ))}
    </div>
  );
}
