// «Night desk»: the folder names stay as the pages' vocabulary, but each now names a role (styles.css):
// coral = red (urgent, late, failed), saffron = gold (waiting for a person), cobalt / jade / orchid / ink = quiet.
// Class names are written out in full so Tailwind keeps them.
import type { IconName } from "./Icon";

export type Folder = "cobalt" | "coral" | "jade" | "saffron" | "orchid" | "ink";

export const FOLDER: Record<Folder, {
  tile: string; onTile: string; bg: string; ink: string; mark: string; color: string; tileColor: string;
}> = {
  cobalt: { tile: "bg-cobalt-tile", onTile: "text-white", bg: "bg-cobalt-bg", ink: "text-cobalt-ink", mark: "bg-cobalt", color: "var(--cobalt)", tileColor: "var(--cobalt-tile)" },
  coral: { tile: "bg-coral-tile", onTile: "text-white", bg: "bg-coral-bg", ink: "text-coral-ink", mark: "bg-coral", color: "var(--coral)", tileColor: "var(--coral-tile)" },
  jade: { tile: "bg-jade-tile", onTile: "text-white", bg: "bg-jade-bg", ink: "text-jade-ink", mark: "bg-jade", color: "var(--jade)", tileColor: "var(--jade-tile)" },
  saffron: { tile: "bg-saffron-tile", onTile: "text-on-accent", bg: "bg-saffron-bg", ink: "text-saffron-ink", mark: "bg-saffron", color: "var(--saffron)", tileColor: "var(--saffron-tile)" },
  orchid: { tile: "bg-orchid-tile", onTile: "text-white", bg: "bg-orchid-bg", ink: "text-orchid-ink", mark: "bg-orchid", color: "var(--orchid)", tileColor: "var(--orchid-tile)" },
  ink: { tile: "bg-ink-tile", onTile: "text-white", bg: "bg-grey-bg", ink: "text-ink", mark: "bg-ink", color: "var(--ink)", tileColor: "var(--side)" },
};

export const SECTIONS: Record<string, { folder: Folder; icon: IconName }> = {
  "/": { folder: "ink", icon: "home" },
  "/tasks": { folder: "coral", icon: "tasks" },
  "/appointments": { folder: "cobalt", icon: "calendar" },
  "/receipts": { folder: "jade", icon: "wallet" },
  "/services": { folder: "saffron", icon: "file-text" },
  "/urf": { folder: "orchid", icon: "scale" },
  "/chats": { folder: "jade", icon: "chat" },
  "/reports": { folder: "cobalt", icon: "reports" },
  "/settings": { folder: "ink", icon: "settings" },
};
