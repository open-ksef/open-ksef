#!/bin/sh
set -euf
out=/etc/nginx/admin-allow.conf
: > "$out"
for source in $(echo "${KEYCLOAK_ADMIN_ALLOW:-}" | tr ',' ' '); do
    if [ "$source" = all ]; then
        echo "$0: KEYCLOAK_ADMIN_ALLOW entry 'all' would open /auth/admin to everyone, skipped" >&2
        continue
    fi
    cp "$out" "$out.ok"
    echo "allow $source;" >> "$out"
    if ! nginx -t -q 2>/dev/null; then
        mv "$out.ok" "$out"
        echo "$0: KEYCLOAK_ADMIN_ALLOW entry '$source' is not a valid nginx allow address, skipped" >&2
    fi
done
rm -f "$out.ok"
