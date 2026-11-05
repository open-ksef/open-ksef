#!/bin/sh
set -eu
out=/etc/nginx/admin-allow.conf
: > "$out"
for source in $(echo "${KEYCLOAK_ADMIN_ALLOW:-}" | tr ',' ' '); do
    echo "allow $source;" >> "$out"
done
