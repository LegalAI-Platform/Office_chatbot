/**
 * v1.110 (the owner: «لما يدوس علي اللوجو يعمل ريلود»): the browser loads the panel again at `to`, as its own reload button
 * does: the newest build, and everything asked again. On that page already, the same page again.
 */
export function reloadAt(to: string): void {
  if (window.location.pathname === to) window.location.reload();
  else window.location.assign(to);
}
