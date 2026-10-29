# The server (TASK 14)

The test server: a Hostinger VPS **KVM 2** (2 vCPU, 8 GB, 96 GB), Ubuntu 24.04, Paris. It runs the same stack as
the owner's PC, from the images the CI published (`ghcr.io/legalai-platform/office-*`), never built there. Still the
test setup: Meta's test number and the test recipients only. The real number and clients come in TASK 14 part 2.

Nothing on it is reachable from the internet but SSH (by key). Meta reaches n8n's webhook through the Cloudflare
tunnel (it goes out from the server); the panel, Chatwoot and n8n's editor are reached through SSH (step 7).

## 1. Hostinger

Plain OS, Ubuntu 24.04 LTS, the owner's SSH **public** key (`%USERPROFILE%\.ssh\id_ed25519.pub`) added at setup, a
generated root password kept by the owner (only for Hostinger's browser console). No Monarx, no Docker manager.
The free domain is not claimed yet (claiming it costs its price if the VPS is refunded within 30 days).

## 2. Setup (once, from the PC, PowerShell)

```powershell
cd K:\law-office-whatsapp
scp deploy/server-setup.sh root@<server>:/root/
ssh root@<server> "bash /root/server-setup.sh"
ssh office@<server> "whoami; docker --version"
```

`deploy/server-setup.sh`: updates and automatic security updates; the user `office`; SSH by key only; a firewall
letting in SSH only, and fail2ban; Docker from Docker's repository; Cairo time; 2 GB of swap.

## 3. The repository and GHCR (on the server, as `office`)

```bash
git clone https://github.com/LegalAI-Platform/Office_chatbot.git /opt/office
```

The images are private. On GitHub (the owner's account): Settings → Developer settings → Personal access tokens →
Tokens (classic) → a token with **`read:packages` only**, no expiry beyond a year. Then, typed by the owner:

```bash
docker login ghcr.io -u <github-user>      # paste the token as the password; it stays in ~/.docker/config.json
```

When the repository becomes private, the clone needs a read-only deploy key instead of https.

## 4. The data and .env (the stack moves, exactly as it is)

On the PC (Git Bash), the stack stopped, so nothing changes while it is copied:

```bash
docker compose --profile tunnel down
bash deploy/volumes.sh export /k/cache/move
```

From the PC (PowerShell), the owner copies `.env` and the archives; they hold every secret and the client data,
so scp only, and both are deleted from the PC's temporary folder afterwards:

```powershell
scp K:\law-office-whatsapp\.env office@<server>:/opt/office/.env
scp -r K:\cache\move office@<server>:/home/office/move
```

On the server (`ssh office@<server>`):

```bash
cd /opt/office && chmod 600 .env
bash deploy/volumes.sh import /home/office/move && rm -rf /home/office/move
```

The same `.env` on both: the same passwords open the same databases, the same `N8N_ENCRYPTION_KEY` opens n8n's
credentials, the same `BACKUP_ENCRYPTION_KEY` reads the backups. `deploy.sh` adds `OFFICE_*_IMAGE` and
`OFFICE_PULL_POLICY=missing`; `tunnel-refresh.sh` sets `WEBHOOK_URL`.

## 5. Deploy

```bash
cd /opt/office && bash deploy/deploy.sh            # the latest main
bash deploy/deploy.sh 1a2b3c4                      # one commit exactly, or back to an earlier one
```

The images of that commit, a backup, the migrations, the stack, the healthcheck; a failed healthcheck puts the
previous version back. The tunnel is not restarted, so Meta's webhook stays.

## 6. Meta's webhook on the server

```bash
bash scripts/linux/tunnel-refresh.sh
```

Meta sends to **one** URL: from now on the server answers WhatsApp, not the PC. On the PC keep the tunnel down
(`docker compose --profile tunnel down`); the PC's own stack can still run for development. Run it again after a
restart of the server or of the tunnel (a Quick Tunnel gets a new link; the domain and a Named Tunnel end that).

## 7. The panel, Chatwoot and n8n from the PC

```powershell
ssh -N -L 8080:127.0.0.1:8080 -L 3000:127.0.0.1:3000 -L 5678:127.0.0.1:5678 office@<server>
```

While it runs: the panel http://127.0.0.1:8080, Chatwoot http://127.0.0.1:3000, n8n http://127.0.0.1:5678, the same
addresses as on the PC (stop the PC's own stack first, or its ports are taken).

## 8. The nightly job

```bash
crontab -e     # as office
0 3 * * * /opt/office/scripts/linux/nightly.sh >> /home/office/nightly.log 2>&1
```
