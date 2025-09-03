Docker Deployment for Production

This guide describes how to deploy the webapp and catalog API on a Proxmox LXC using Docker and Compose. It matches the repo’s production files and conventions.

Prereqs on the LXC
- Enable “Nesting” in Proxmox for the container (Options → Features).
- Install Docker and Compose plugin (Debian/Ubuntu):
  - sudo apt-get update
  - sudo apt-get install -y docker.io docker-compose-plugin
  - sudo usermod -aG docker $USER && newgrp docker
- Verify: docker compose version

Get the code onto the LXC
- Clone or sync the repo to the LXC, e.g.:
  - rsync -av --delete ./bwkt-webapp/ user@lxc:/opt/bwkt-webapp
  - or git clone <repo> /opt/bwkt-webapp

Build and run (production)
- From the repo root on the LXC:
  - docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
- What this does:
  - Builds images in Release mode (via build args) and starts both services.
  - Webapp is published on host port 80 → container 8080.
  - catalog-api is internal only (no host port).
  - A named volume `web-data` persists the webapp data at `/app/data`.

Verify
- Check containers: docker ps
- Logs (follow): docker compose logs -f
- Open the UI: http://<lxc-ip>/

Updating to a new version
- Pull new code and rebuild in place:
  - git pull
  - docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
- Compose recreates only what changed; containers are restarted after images are ready.

Stopping services
- docker compose -f docker-compose.yml -f docker-compose.prod.yml down

Alternative workflow: build elsewhere, deploy images
- Push to a registry and reference tags in compose, or save+copy:
  - docker save -o webapp.tar bwkt-webapp:dev
  - docker save -o api.tar bwkt-catalog-api:dev
  - scp to the LXC and load:
    - docker load -i webapp.tar
    - docker load -i api.tar
  - Then run the same compose up command above.

TLS and domain
- Easiest: run a reverse proxy (Caddy/Nginx) on the LXC that terminates TLS and forwards to `webapp:8080`.
- If exposing directly on port 80, ensure upstream firewall/NAT allows HTTP (and 443 if adding TLS later).

Health, logs, troubleshooting
- Health: docker ps, docker inspect <container>
- Logs: docker compose logs -f webapp | catalog-api
- If Docker fails to start, run scripts/docker-diagnose.sh and review docker-output.txt
