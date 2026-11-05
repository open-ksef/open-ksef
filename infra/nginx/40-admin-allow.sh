#!/bin/sh
set -eu
out=/etc/nginx/admin-allow.conf
: > "$out"
for source in $(echo "${KEYCLOAK_ADMIN_ALLOW:-}" | tr ',' ' '); do
    cp "$out" "$out.ok"
    echo "allow $source;" >> "$out"
    if ! nginx -t -q 2>/dev/null; then
        mv "$out.ok" "$out"
        echo "$0: KEYCLOAK_ADMIN_ALLOW entry '$source' is not a valid nginx allow address, skipped" >&2
    fi
done
rm -f "$out.ok"
