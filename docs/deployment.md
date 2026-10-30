# Deployment: CI/CD, the server, and the road to production

The single reference for how Hazem is built, shipped and run outside the developer's PC (TASK 14). It describes
what is live today (a **test server**, Meta's test number, test recipients only) and lists, at the end, what changes
for **production** and the final handover. The step-by-step commands are in [`docs/runbooks/server.md`](runbooks/server.md);
this document explains the design, the decisions and the reasons.

> **ملخص بالعربي:** الكود بيتختبر ويتبني على GitHub (CI)، والصور بتترفع private على GHCR. السيرفر (Hostinger
> KVM 2) بيسحب الصور ويشغّلها بس، ومفيش بناء عليه. الدخول على السيرفر بمفتاح SSH بس. الإنترنت بيوصل للسيستم من
> Cloudflare Tunnel بس: `wa.` لـ Meta، و`panel.` للداشبورد، و`chat.` لـ Chatwoot، والداشبورد وChatwoot من مصر
> بس. التنزيل بأمر واحد `bash deploy/deploy.sh`، وبيرجع للنسخة اللي قبلها لوحده لو الفحص فشل. اللي فاضل للتشغيل
> الحقيقي في آخر الملف.

Status on 2026-10-30: everything below is live except where marked *planned*.

---

## 1. The picture

```
 Developer PC ──push──► GitHub ──CI (tests, secret scan, image build)──► merge to main
                                   │
                                   └─ Images workflow ─► ghcr.io/legalai-platform/office-{api,web,tools}:sha-<commit>
                                                                       │ (private, pulled with a read-only token)
 Meta (WhatsApp) ──https──► Cloudflare ──Named Tunnel (outbound from the server)──►  ┌──────────── VPS ────────────┐
 Staff browser  ──https──►  (DNS, TLS, WAF: Egypt only for panel/chat)              │ cloudflared → edge (nginx)  │
                                                                                    │   wa.*    → n8n webhook only │
 Owner (SSH key) ─────────────────────────────── port 22 only ───────────────────►  │   panel.* → web → api       │
                                                                                    │   chat.*  → Chatwoot        │
                                                                                    │ postgres, redis, garage, …  │
                                                                                    └─────────────────────────────┘
```

- **Nothing is built on the server.** GitHub's machines test and build; the server only pulls images and runs them.
  The repository is public for now, so no self-hosted runner is ever used (anyone could run code on it).
- **Nothing listens on the internet** except SSH (key only). The tunnel is an outbound connection from the server
  to Cloudflare; every published service sits behind it and behind the edge.
- **Every container port is bound to 127.0.0.1** (as on the PC); the API has no published port at all.

## 2. Environments

| | Developer PC | Test server (now) | Production (*planned*, §10) |
|---|---|---|---|
| Purpose | write and test code | the bot answering the test numbers, staff testing the panel | the office's clients |
| Images | built locally (`office-*:local`) | GHCR, `sha-<commit>` | GHCR, the same tag tested on the test server |
| WhatsApp | none (Meta sends to one URL: the server) | Meta **test** number | the office's **real** number |
| Who receives | — | `whatsapp.send_restricted=true`, `whatsapp.test_recipients` | everyone (owner's decision) |
| Data | throwaway | moved from the PC on 2026-10-30 (test data) | starts clean, or a decision |
| Public links | none | `wa.` `panel.` `chat.` on the test domain | the production domain |

A developer's PC runs the full stack with its own `.env` (`scripts/*/gen-secrets`), tests everything but live
WhatsApp, and opens pull requests. It never needs the owner's PC or the server.

## 3. CI/CD

### 3.1 CI: every pull request (`.github/workflows/ci.yml`)

| Check | What |
|---|---|
| Backend tests | Postgres 16.15 with the repo's init scripts and random passwords; `dotnet test` (unit + integration on a throwaway `office_test_*` database, including every approved text word for word) |
| Panel tests and build | Node 24.21.0, `npm ci`, typecheck, vitest, build |
| Secret scan | gitleaks 8.21.2 over the **whole history**, `--redact`; `.gitleaks.toml` adds full Egyptian mobile numbers (010/011/012/015); known safe findings pinned in `.gitleaksignore` |
| Image build ×3 | the three Dockerfiles build (not pushed) |

Actions are pinned to commit SHAs (tag in a comment). A red check means do not merge.

### 3.2 Images: every merge to main (`.github/workflows/images.yml`)

The same CI again, then, per image (`api` ← `backend/`, `web` ← `web/`, `tools` ← `infra/tools/`):

- **Built only when its folder changed** since the commit its current `main` image was built from (the image's
  `org.opencontainers.image.revision` label). Otherwise the existing image just gets the new tag (no new space).
  A push whose CI failed published nothing, so its changes are still counted next time.
- Tags: `sha-<7 chars>` (every commit, for exact deploys and rollbacks) and `main` (the latest).
- `linux/amd64`, no provenance/SBOM attestations (one manifest per version).
- **Retention: the last 5 versions** of each package (`actions/delete-package-versions`), because private
  packages get 500 MB on the free plan.
- No secret is in any image: everything comes from the server's `.env` at start-up.

### 3.3 Branch protection

`main` requires the six CI checks, also for admins; no force-push, no deletion. **On the free plan this stops
working once the repository is private** (§9): then the rule is "never merge with a red check", and the server is
still safe because a red CI publishes no image.

## 4. The server

| | |
|---|---|
| Provider / plan | Hostinger VPS **KVM 2**: 2 vCPU, 8 GB RAM, 96 GB NVMe, 8 TB traffic; Paris |
| OS | Ubuntu 24.04 LTS (plain OS, no panel, no Monarx, no Hostinger Docker manager) |
| Bought | 2026-10-29, **1 month** (E£689 + tax, renews at E£1,119/month; 24 months ≈ E£419/month); refundable within 30 days (one VPS refund per 180 days; a claimed free domain is deducted) |
| Address | in Hostinger's panel and the owner's notes (not in this public repo) |
| Users | `root` (key only; password only in Hostinger's browser console), `office` (runs the stack, in the `docker` group) |
| Stack | `/opt/office`: a git clone of this repository, checked out at the deployed commit; `.env` (mode 600) |

**Hardening** (`deploy/server-setup.sh`, idempotent, run once as root): system updates and automatic security
updates; SSH by key only (`sshd_config.d/00-office.conf`: no passwords, root by key only, no agent or X11
forwarding); ufw lets in SSH only; fail2ban on SSH; Docker Engine + Compose from Docker's own repository; Cairo
time; 2 GB swap. Measured load: the whole stack idles at about 1.7 GB of RAM.

**Monitoring on the box**: `scripts/linux/healthcheck.sh` (157 checks), the nightly job (§7) reporting failures to
the panel's bell and by e-mail, and `docker compose ps`.

## 5. Network and access

### 5.1 Domain and DNS

The test domain **`hazem-office.tech`** was claimed free with the VPS (registered at Hostinger, 1 year, renews at
about $64/year; the registrant e-mail had to be verified by 2026-11-13). Its nameservers point to **Cloudflare**
(Free plan), which holds the DNS. No record points at the server's IP: the only records are the tunnel's CNAMEs, so
the server's address stays hidden behind Cloudflare.

### 5.2 Cloudflare Tunnel

A **Named Tunnel** `hazem-server` runs on the server as the `tunnel-named` container (profile `tunnel-named`); its
token is `CLOUDFLARE_TUNNEL_TOKEN` in the server's `.env`. Its routes ("Published applications"), all to the edge:

| Hostname | Service | Reaches (`infra/edge/nginx.conf`) |
|---|---|---|
| `wa.hazem-office.tech` | `http://edge:8081` | Meta's webhook only; `/healthz`; every other path 404 (n8n's login and API are never exposed) |
| `panel.hazem-office.tech` | `http://edge:8081` | the panel (`web`), which itself forwards `/api/panel` only to the API |
| `chat.hazem-office.tech` | `http://edge:8081` | Chatwoot, with its `/cable` WebSocket |

The edge takes the visitor's address from Cloudflare's `CF-Connecting-IP`; the panel's nginx trusts the edge's
`X-Forwarded-For`/`-Proto` from the Docker networks only (`real_ip`), so the API's rate limits count each visitor and
its cookies are `Secure`. The old Quick Tunnel (`tunnel` profile) is stopped; it is only for a machine without the
domain.

### 5.3 Who can open what

- **Panel and Chatwoot:** Cloudflare WAF custom rule *"Egypt only for the panel"*: `http.host in {panel.… chat.…}
  and ip.src.country ne "EG"` → Block. `wa.` stays open (Meta sends from abroad). Someone travelling cannot sign in
  until the rule is paused. Bot Fight Mode is **off** on purpose (on the free plan it cannot exempt `wa.` and may
  challenge Meta's webhook).
- **Then the panel's own guards:** password, the authenticator app (optional for everyone, owner's decision v1.16;
  `PANEL_MANAGERS_NEED_2FA=true` would require it for managers), sign-in rate limits, new accounts waiting for a
  manager's approval. Chatwoot's sign-up is off.
- *Planned:* Cloudflare Access (Zero Trust, free up to 50 people) in front of `panel.` and `chat.`, an allow-list of
  staff e-mails with a one-time PIN, so nobody else even sees a sign-in page. It needs a card on the Cloudflare
  account (nothing is charged); there was none on 2026-10-30.
- **SSH:** the owner's key only. Adding a person's key to `office` gives them everything (`.env`, client data), so the
  owner deploys and others open pull requests.
- **Panel accounts:** created by a manager in «الحسابات» (a temporary password shown once, changed at first sign-in).

## 6. Deploying

```bash
ssh office@<server>
cd /opt/office && bash deploy/deploy.sh          # the latest main
bash deploy/deploy.sh 1a2b3c4                    # one commit exactly, or back to an earlier one
```

`deploy/deploy.sh`: the commit's files (`git checkout --detach`) → its images in `.env` (`OFFICE_*_IMAGE`,
`OFFICE_PULL_POLICY=missing`) and `docker compose pull` → a backup → migrations (`dbmate`, forward only) →
`docker compose up -d --wait` → **n8n's workflows imported again if the commit changed `n8n/`** (n8n runs the copy in
its own database; Meta's link is checked afterwards) and **the edge recreated if it changed `infra/edge/`** → a log
scan and the healthcheck. A failed healthcheck puts the previous version back (database changes stay). The tunnel is
never restarted, so Meta's link does not change.

The usual flow: a pull request → green CI → merge → wait for the Images run (~6 min) → `deploy.sh` on the server.

**Moving data between machines** (done once, PC → server): `deploy/volumes.sh export|import` copies the nine data
volumes exactly (checksums; refuses to overwrite a volume holding data; refuses while the stack runs). Both sides
need the same `.env` (the same passwords and keys open the same data).

## 7. Backups

- **Nightly at 03:00** (cron of `office`): `scripts/linux/nightly.sh`: encrypted backup of the three databases and
  the client files to the `office-backups` bucket (30 days), healthcheck, log scan for secrets and phone numbers,
  and a restore drill every 28 days. Failures reach the managers (bell + e-mail). Log: `/home/office/nightly.log`.
- Encryption key `BACKUP_ENCRYPTION_KEY`: without it no backup can be read; the owner keeps a copy off the machine.
- `deploy.sh` also backs up before every deploy.
- *Planned:* an off-site copy (Cloudflare R2, free up to 10 GB) — today the backups are on the same disk as the
  data, so losing the VPS loses both. Until then, `backup-export` to another machine from time to time.

Details: [`runbooks/backup-restore.md`](runbooks/backup-restore.md).

## 8. Secrets and accounts

All secrets live in `.env` on the machine that runs the stack; never in the repo, the images, the chat or a
screenshot. On the server the **owner types them himself** (e.g. `read -rsp "Token: " t && sed …`, nothing on
screen). Rotation: [`runbooks/secrets.md`](runbooks/secrets.md).

| What | Where | Note |
|---|---|---|
| GHCR read token | `docker login ghcr.io` as `office` (`~/.docker/config.json`) | GitHub classic PAT, **`read:packages` only** (fine-grained tokens do not work with GHCR); renew before it expires |
| Cloudflare tunnel token | `.env` `CLOUDFLARE_TUNNEL_TOKEN` | "Rotate token" in Cloudflare invalidates it |
| Anthropic key (classifier) | `.env` | expires 2026-11-27 |
| OpenAI key (fallback, voice) | `.env` | out of credit on 2026-10-30: voice notes are not transcribed |
| Meta, Google Calendar, SMTP, databases, panel | `.env` / n8n credentials | unchanged from the PC |

| Account | Used for |
|---|---|
| GitHub `LegalAI-Platform` (Free) | code, CI, GHCR |
| Hostinger | the VPS and the domain |
| Cloudflare (Free) | DNS, tunnel, WAF rule |
| Meta (test app) | WhatsApp test number |

## 9. When the repository becomes private (free plan)

The owner's plan. Nothing breaks, but:

- **Branch protection stops being enforced** (Team plan ≈ $4/member/month brings it back). The server stays safe:
  no green CI, no image.
- **Actions: 2,000 minutes/month.** A pull request costs ~12 billed minutes, a merge ~18.
- **GHCR: 500 MB storage, 1 GB/month transfer** for private packages: why only changed images are built and only 5
  versions are kept. Over a limit GitHub does not charge without a payment method; pushes just stop until the month
  ends, the server keeps running.
- **Environment approvals are unavailable**, so deploys stay manual (`deploy.sh` by the owner), or a
  `workflow_dispatch` button later.
- The server's clone needs a **read-only deploy key** instead of https; collaborators need repository access.

## 10. Production and the final handover (*planned*, TASK 14 part 2)

A checklist, in order. Each item is the owner's decision where it says so.

1. **Staging and production apart.** On the same VPS (two stacks, two `.env`, two databases; KVM 2 holds both at
   ~4–6 GB) or a second VPS. Every release goes to staging first; the **same image tag** is deployed to production
   only after the owner tests it.
2. **The production domain.** Keep `hazem-office.tech` (renews ~$64/year) or move to a `.com` (~$10/year at
   Cloudflare Registrar). The office's public site `saadfathy.com` stays separate.
3. **Meta for real.** The office's own number on a verified business, approved templates (reminders outside the
   24-hour window), the webhook on the production `wa.` link (`tunnel-refresh.sh --url`).
4. **Open to clients** — owner's explicit decision only: `whatsapp.send_restricted=false` (and Hazem's own
   recipients setting), with the healthcheck's warning acknowledged.
5. **Real office details** in the panel (المواعيد ← بيانات المكتب): InstaPay, wallet, address, map, Meet link,
   phone (the healthcheck warns while they are test values).
6. **Off-site backups** (R2) and a restore tested from them.
7. **Access:** Cloudflare Access on `panel.`/`chat.`; a decision on the authenticator (managers at least).
8. **Keys:** fresh secrets for production (`gen-secrets`), every key that ever went through a chat rotated, the
   Anthropic and OpenAI keys funded and renewed, GHCR token and tunnel token renewed.
9. **The repository private** (§9) with a deploy key on the server.
10. **Handover:** this document, `runbooks/server.md`, the newest `docs/sessions/` report and `docs/decisions/` (the
    source of truth for the office's rules); the accounts in §8 transferred or shared as the owner decides; the
    VPS on a longer term.

## 11. Known behaviours and troubleshooting

| Symptom | Cause / fix |
|---|---|
| `tunnel-refresh` ends with `ERROR: Meta callback is ''` | Meta checks the link before n8n is fully up and n8n does not retry. Run `n8n-import.sh`, then `tunnel-refresh.sh --url https://wa.<domain>` again (it worked the second time every time). `deploy.sh` does this by itself. |
| WhatsApp stops arriving after a restart | Only with a Quick Tunnel (new link each start). With the domain it does not happen; check `docker compose --profile tunnel-named ps` and the tunnel's page in Cloudflare (Healthy). |
| `Provided Tunnel token is not valid` | The `.env` holds something else (the tunnel ID, or the whole `docker run …` command): only the part starting `eyJ…`. |
| The panel 404s on `panel.` | The route is missing in the tunnel, or the edge runs an old config: `docker compose --profile tunnel --profile tunnel-named up -d edge`. |
| `deploy.sh`: images not on GHCR | The Images run for that commit has not finished or failed; or the GHCR login expired (`docker login ghcr.io`). |
| The healthcheck fails "nightly … within 26 hours" | The machine or the stack was off; run `scripts/linux/nightly.sh` once. |
| Nobody abroad can open the panel | The WAF rule (Egypt only): pause it in Cloudflare → Security rules. |

---

Related: [`runbooks/server.md`](runbooks/server.md) (commands) · [`runbooks/whatsapp.md`](runbooks/whatsapp.md) ·
[`runbooks/backup-restore.md`](runbooks/backup-restore.md) · [`runbooks/secrets.md`](runbooks/secrets.md) ·
[`security/threat-model.md`](security/threat-model.md) · [`env-variables.md`](env-variables.md)
