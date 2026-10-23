// The owner's own images (cut from his approved mockups), one cut per theme. Both are in the page and
// CSS shows the one for the current theme, so the switch needs no script and follows the device.
const FILES = import.meta.glob<string>("../../assets/owner/*/*.png", { eager: true, import: "default" });

export type OwnerImage =
  | "logo_emblem"
  | "hero"
  | "sidebar_art"
  | "user_profile"
  | "top_calendar"
  | "top_search"
  | "nav_clients"
  | "nav_tasks"
  | "nav_appointments"
  | "nav_documents"
  | "nav_reports"
  | "nav_settings"
  | "stat_calendar_blue"
  | "stat_card_green"
  | "stat_alert_yellow"
  | "stat_clock_red"
  | "alert_urgent_red"
  | "alert_task_orange"
  | "alert_receipt_green"
  | "alert_message_blue";

const src = (theme: "light" | "dark", name: OwnerImage) => FILES[`../../assets/owner/${theme}/${name}.png`] ?? "";

/** Decorative unless `alt` is given. */
export function OwnerImg({ name, className = "", alt = "" }: { name: OwnerImage; className?: string; alt?: string }) {
  return (
    <>
      <img src={src("light", name)} alt={alt} aria-hidden={alt ? undefined : true} className={`only-light ${className}`} draggable={false} />
      <img src={src("dark", name)} alt="" aria-hidden className={`only-dark ${className}`} draggable={false} />
    </>
  );
}
