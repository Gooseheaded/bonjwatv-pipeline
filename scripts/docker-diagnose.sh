#!/usr/bin/env bash
# Collects Docker daemon diagnostics and writes everything to docker-output.txt
# Safe to run repeatedly; read-only commands except service status/log access.

set -u

LOG="docker-output.txt"
exec >"${LOG}" 2>&1

section() {
  echo
  echo "===== $1 ====="
}

echo "Docker diagnostics started at: $(date -Is)"
echo "User: $(id)"

section "System Info"
uname -a || true
command -v hostnamectl >/dev/null 2>&1 && hostnamectl status || true
[ -r /etc/os-release ] && cat /etc/os-release || true

section "Docker/Compose Versions"
command -v docker >/dev/null 2>&1 && docker --version || echo "docker not found in PATH"
command -v docker-compose >/dev/null 2>&1 && docker-compose --version || echo "docker-compose (v1) not found"
docker compose version || echo "docker compose (v2 plugin) not available"

section "Process Check"
ps -ef | grep -E "dockerd|containerd" | grep -v grep || true

section "Sockets & PID files"
ls -l /var/run/docker.sock 2>/dev/null || echo "/var/run/docker.sock missing"
ls -l /run/containerd/containerd.sock 2>/dev/null || echo "/run/containerd/containerd.sock missing"
ls -l /var/run/docker.pid 2>/dev/null || echo "/var/run/docker.pid missing"

section "Service Status: containerd"
sudo systemctl status containerd || true

section "Logs: containerd (last 300 lines)"
sudo journalctl -u containerd -n 300 --no-pager || true

section "Service Status: docker"
sudo systemctl status docker || true

section "Logs: docker.service (last 300 lines + recent errors)"
sudo journalctl -xeu docker.service -n 300 --no-pager || true

section "Docker Config (/etc/docker/daemon.json)"
if [ -f /etc/docker/daemon.json ]; then
  echo "Contents of /etc/docker/daemon.json:"
  sudo cat /etc/docker/daemon.json || true
  if command -v jq >/dev/null 2>&1; then
    echo
    echo "Validated JSON via jq:"
    sudo jq . /etc/docker/daemon.json || true
  else
    echo "jq not installed; skipping JSON validation"
  fi
else
  echo "no daemon.json"
fi

section "Storage & Kernel"
df -h || true
df -i || true
echo
echo "Loaded kernel modules (overlay/br_netfilter):"
lsmod | grep -E "(^overlay|br_netfilter)" || echo "overlay/br_netfilter not listed"
echo "Kernel: $(uname -r)"

section "Cgroups & Mounts"
mount | grep -E "cgroup|overlay" || true

section "Docker Info (may fail if daemon is down)"
docker info || true

section "Compose File Summary"
if [ -f docker-compose.yml ]; then
  echo "docker-compose.yml present at: $(pwd)/docker-compose.yml"
  # Print top-level keys to help spot deprecated 'version' key
  if command -v yq >/dev/null 2>&1; then
    yq 'keys' docker-compose.yml || true
  else
    head -n 50 docker-compose.yml || true
  fi
else
  echo "No docker-compose.yml in current directory: $(pwd)"
fi

section "Done"
echo "Diagnostics finished at: $(date -Is)"
echo "Output saved to: ${LOG}"

