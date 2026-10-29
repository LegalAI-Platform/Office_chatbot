// The panel's first request, started by its own tiny entry in index.html (run before React and the rest of the code
// are evaluated): who is signed in. useMe() picks the answer up instead of asking again, so the first screen waits for
// one round trip less (idea from PR #14).
import { askMeEarly } from "./shared/api/client";

if (!/^\/(login|register|verify-email|forgot-password)\b/.test(location.pathname)) askMeEarly();
