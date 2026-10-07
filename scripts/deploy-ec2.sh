#!/usr/bin/env bash
set -euo pipefail

: "${IMAGE:?IMAGE must be supplied by the deployment workflow}"

APP_NAME="webapp-api"
DEPLOY_DIR="$HOME/$APP_NAME"
ACTIVE_PORT_FILE="$DEPLOY_DIR/active-port"
BLUE_PORT=8081
GREEN_PORT=8082

mkdir -p "$DEPLOY_DIR"

if ! command -v docker >/dev/null || ! command -v curl >/dev/null || ! command -v nginx >/dev/null; then
  echo "Docker, curl and Nginx must be installed before the first deployment." >&2
  exit 1
fi

active_port=""
if [ -f "$ACTIVE_PORT_FILE" ]; then
  active_port=$(cat "$ACTIVE_PORT_FILE")
fi

if [ "$active_port" = "$BLUE_PORT" ]; then
  next_port=$GREEN_PORT
  next_name="$APP_NAME-green"
  previous_name="$APP_NAME-blue"
else
  next_port=$BLUE_PORT
  next_name="$APP_NAME-blue"
  previous_name="$APP_NAME-green"
fi

docker pull "$IMAGE"
docker rm -f "$next_name" 2>/dev/null || true
docker volume create "$APP_NAME-data" >/dev/null
docker run -d --restart unless-stopped \
  --name "$next_name" \
  --publish "127.0.0.1:${next_port}:80" \
  --mount "type=volume,source=${APP_NAME}-data,target=/data" \
  --env DatabasePath=/data/webapp.db \
  "$IMAGE"

for _ in $(seq 1 30); do
  if curl --fail --silent "http://127.0.0.1:${next_port}/api/health" >/dev/null; then
    break
  fi
  sleep 2
done

if ! curl --fail --silent "http://127.0.0.1:${next_port}/api/health" >/dev/null; then
  docker logs "$next_name" || true
  docker rm -f "$next_name" || true
  echo "The candidate container did not become healthy." >&2
  exit 1
fi

temp_config=$(mktemp)
cat > "$temp_config" <<EOF
server {
    listen 80 default_server;
    server_name _;

    location / {
        proxy_pass http://127.0.0.1:${next_port};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

sudo -n install -m 644 "$temp_config" /etc/nginx/conf.d/webapp-api.conf
rm -f "$temp_config"
sudo -n nginx -t
sudo -n systemctl reload nginx

printf '%s\n' "$next_port" > "$ACTIVE_PORT_FILE"
docker rm -f "$previous_name" 2>/dev/null || true
docker image prune -f >/dev/null

echo "Deployed ${IMAGE} on local port ${next_port}."
