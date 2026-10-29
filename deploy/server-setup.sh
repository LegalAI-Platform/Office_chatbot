#!/bin/bash
# One-time setup of the office's server (TASK 14): Ubuntu 24.04 on the VPS, run as root from the owner's PC:
#   ssh root@<server> 'bash -s' < deploy/server-setup.sh
# Safe to run again: each step checks what is already there.
#
# What it does:
#   1. updates the system and turns on the automatic security updates
#   2. a user «office» (the stack runs as it, in /opt/office), with root's SSH keys
#   3. SSH by key only: no password login for anyone (Hostinger's browser console still works with the root password)
#   4. a firewall that lets in SSH only (the tunnel goes out, so nothing else needs to come in) and fail2ban on SSH
#   5. Docker Engine and Compose from Docker's own repository (not the snap, not Hostinger's)
#   6. Cairo time, and 2 GB of swap so a spike never kills a database
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

[[ $EUID -eq 0 ]] || { echo "run as root" >&2; exit 1; }
. /etc/os-release
[[ "$ID" == "ubuntu" && "$VERSION_ID" == "24.04" ]] || { echo "made for Ubuntu 24.04, this is $PRETTY_NAME" >&2; exit 1; }
[[ -s /root/.ssh/authorized_keys ]] || { echo "root has no SSH key: add one in Hostinger before key-only login is turned on" >&2; exit 1; }

step() { echo; echo "==== $*"; }

step "1/6 System updates and automatic security updates"
apt-get update -q
apt-get -y -q -o Dpkg::Options::=--force-confold upgrade
apt-get install -y -q ca-certificates curl gnupg git jq unattended-upgrades fail2ban python3-systemd ufw
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

step "2/6 The «office» user and /opt/office"
id office >/dev/null 2>&1 || useradd --create-home --shell /bin/bash office
install -d -m 700 -o office -g office /home/office/.ssh
install -m 600 -o office -g office /root/.ssh/authorized_keys /home/office/.ssh/authorized_keys
install -d -m 750 -o office -g office /opt/office

step "3/6 SSH by key only"
# first in sshd_config.d, so it wins over Hostinger's and cloud-init's own files (sshd keeps the first value it reads)
cat > /etc/ssh/sshd_config.d/00-office.conf <<'EOF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
PermitEmptyPasswords no
X11Forwarding no
AllowAgentForwarding no
MaxAuthTries 3
EOF
sshd -t
systemctl try-reload-or-restart ssh
sshd -T | grep -E '^(passwordauthentication|permitrootlogin|kbdinteractiveauthentication) '

step "4/6 Firewall (SSH only) and fail2ban"
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null
ufw allow OpenSSH >/dev/null
ufw --force enable >/dev/null
ufw status verbose | head -8
cat > /etc/fail2ban/jail.d/sshd.local <<'EOF'
[sshd]
enabled = true
backend = systemd
maxretry = 5
findtime = 10m
bantime = 1h
EOF
systemctl enable --now fail2ban >/dev/null
systemctl restart fail2ban

step "5/6 Docker Engine and Compose"
if ! command -v docker >/dev/null 2>&1; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $VERSION_CODENAME stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
# the containers' own logs are capped in docker-compose.yml; this caps anything started by hand
[[ -f /etc/docker/daemon.json ]] || echo '{ "log-driver": "json-file", "log-opts": { "max-size": "10m", "max-file": "5" } }' > /etc/docker/daemon.json
systemctl enable --now docker >/dev/null
systemctl restart docker
usermod -aG docker office
docker --version
docker compose version

step "6/6 Cairo time and swap"
timedatectl set-timezone Africa/Cairo
if ! swapon --show | grep -q /swapfile; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile >/dev/null && swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
echo 'vm.swappiness=10' > /etc/sysctl.d/90-office.conf
sysctl -q --system
free -h | sed -n '1,3p'

echo
echo "Done. Next: as «office» (ssh office@<server>), clone the repository into /opt/office (docs/runbooks/server.md)."
[[ -f /var/run/reboot-required ]] && echo "The updates need a restart: reboot when convenient." || true
