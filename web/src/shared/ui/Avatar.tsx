import { useState } from "react";

/**
 * A panel account's photo (migration 0036) when it has one, otherwise the initials of the name in a
 * gold-ringed circle (the theme's colours, light and dark). Clients never have a photo: no client
 * photos are kept. A photo that does not load falls back to the initials.
 */
export function Avatar({ name, photo, className = "size-9 text-sm" }: { name: string; photo?: string | null; className?: string }) {
  const [broken, setBroken] = useState<string | null>(null);
  if (photo && broken !== photo) {
    return (
      <img src={photo} alt="" aria-hidden draggable={false} onError={() => setBroken(photo)}
        className={`shrink-0 rounded-full border border-gold-line bg-gold-tint object-cover ${className}`} />
    );
  }
  return (
    <span className={`grid shrink-0 place-items-center rounded-full border border-gold-line bg-gold-tint font-bold text-gold-ink ${className}`} aria-hidden>
      {initials(name)}
    </span>
  );
}

/** One word: its first two letters; more: the first letter of the first two (apart, Arabic letters would join). */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return parts.length === 0 ? "؟" : parts.length === 1 ? parts[0]!.slice(0, 2) : `${parts[0]![0]} ${parts[1]![0]}`;
}
