FROM nginx:1.27-alpine

# Nginx config is baked in – no local file mount needed.
COPY default.conf /etc/nginx/conf.d/default.conf
COPY --chmod=755 40-admin-allow.sh /docker-entrypoint.d/40-admin-allow.sh
COPY --chmod=755 41-external-scheme.sh /docker-entrypoint.d/41-external-scheme.sh
