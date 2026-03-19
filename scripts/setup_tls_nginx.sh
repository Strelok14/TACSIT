#!/usr/bin/env bash
set -euo pipefail

# Быстрая настройка TLS + Nginx reverse proxy для T.A.C.I.D.
# Запускать на Linux сервере с правами root.

DOMAIN="${1:-}"
if [[ -z "$DOMAIN" ]]; then
  echo "Usage: sudo ./scripts/setup_tls_nginx.sh <domain>"
  exit 1
fi

apt-get update
apt-get install -y nginx certbot python3-certbot-nginx

mkdir -p /var/www/certbot
cp scripts/nginx/tacid.conf /etc/nginx/sites-available/tacid.conf
sed -i "s/tacid.example.com/${DOMAIN}/g" /etc/nginx/sites-available/tacid.conf

ln -sf /etc/nginx/sites-available/tacid.conf /etc/nginx/sites-enabled/tacid.conf
rm -f /etc/nginx/sites-enabled/default

nginx -t
systemctl reload nginx

certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos -m "admin@${DOMAIN}" --redirect

nginx -t
systemctl restart nginx

echo "TLS setup complete for ${DOMAIN}"
