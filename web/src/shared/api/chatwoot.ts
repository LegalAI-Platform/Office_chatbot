// Chatwoot, the staff's conversation screen (TASK 07b, T-24). The panel opens it already signed in
// with a one-time link (no second password), at the client's conversation when a task (or an appointment) is given.
// Shared: the tasks feature and the conversations page both open it.
import { api } from "./client";

export interface ChatwootLinks {
  login_url: string;
  conversation_url?: string | null;
}

/** How long Chatwoot needs to finish signing in before the conversation can open. */
const SIGN_IN_MS = 2500;

/**
 * Opens a new tab at once (a browser only allows that inside the click), then points it at the
 * sign-in link, then at the conversation. Returns the error to show, if any.
 */
export async function openChatwoot(of?: string | { appointmentId: string }): Promise<unknown> {
  const tab = window.open("about:blank", "_blank");
  // Chatwoot's page must not reach back into the panel (what noopener does; noopener itself would
  // give no handle to point the tab with).
  if (tab) tab.opener = null;
  try {
    const links = await api<ChatwootLinks>("POST", "/chatwoot/open",
      typeof of === "string" ? { task_id: of } : of ? { appointment_id: of.appointmentId } : {});
    if (!tab) {
      window.location.assign(links.login_url);
      return undefined;
    }
    tab.location.href = links.login_url;
    if (links.conversation_url) {
      const next = links.conversation_url;
      setTimeout(() => {
        try {
          tab.location.href = next;
        } catch {
          // the person closed the tab
        }
      }, SIGN_IN_MS);
    }
    return undefined;
  } catch (e) {
    tab?.close();
    return e;
  }
}
