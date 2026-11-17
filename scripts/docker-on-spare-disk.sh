#!/bin/bash
set -euo pipefail

config=/etc/docker/daemon.json
current=$(sudo cat "$config" 2>/dev/null || echo '{}')

sudo systemctl stop docker.socket docker
sudo mkdir -p /mnt/docker
jq '. + {"data-root": "/mnt/docker"}' <<< "$current" | sudo tee "$config" >/dev/null
sudo systemctl start docker
df -h / /mnt
