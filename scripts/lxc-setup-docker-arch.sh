#!/usr/bin/env bash
set -euo pipefail

# Prepare an Arch Linux LXC container to run Docker + Compose using fuse-overlayfs.
# Run inside the container as root (or with sudo).

echo "[0/6] Detecting OS…"
if ! grep -qi '^ID=arch' /etc/os-release; then
  echo "WARNING: This script is tuned for Arch Linux. Aborting to be safe." >&2
  exit 2
fi

echo "[1/6] Updating package database…"
pacman -Sy --noconfirm

echo "[2/6] Installing packages: docker, docker-compose-plugin, fuse-overlayfs, docker-buildx"
pacman -S --needed --noconfirm docker docker-compose-plugin fuse-overlayfs docker-buildx || true

echo "[3/6] Configuring Docker to use fuse-overlayfs (works well in LXC)"
mkdir -p /etc/docker
cat > /etc/docker/daemon.json <<'JSON'
{
  "storage-driver": "overlay2",
  "storage-opts": [
    "overlay2.mount_program=/usr/bin/fuse-overlayfs"
  ]
}
JSON

echo "[4/6] Enabling and starting services: containerd + docker"
systemctl enable --now containerd docker

echo "[5/6] Verifying Docker + Compose…"
docker --version || { echo "ERROR: docker not available" >&2; exit 1; }
docker compose version || { echo "ERROR: docker compose plugin not available" >&2; exit 1; }

echo "[6/6] Checking storage driver…"
docker info 2>/dev/null | grep -iE 'Storage Driver|Backing Filesystem' || true

cat <<'NOTE'

Done. Notes:
- If you plan to run Docker as a non-root user, add the user to the docker group:
    usermod -aG docker $SUDO_USER 2>/dev/null || usermod -aG docker $USER
    newgrp docker
- This LXC must be created with Proxmox features enabled on the host:
    pct set <CTID> -features nesting=1,keyctl=1,fuse=1
  Then restart the container:
    pct stop <CTID> && pct start <CTID>
- To deploy this app bundle after running this script:
    cd /opt/bwkt/current && ./run.sh

NOTE

