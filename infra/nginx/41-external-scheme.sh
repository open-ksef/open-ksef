#!/bin/sh
set -eu
out=/etc/nginx/external-scheme.conf
mismatch=/tmp/https-on-http-base.log
case "${APP_EXTERNAL_BASE_URL:-}" in
    https://*) echo 'set $pinned_proto https;' > "$out" ;;
    http://*)
        cat > "$out" <<EOF
set \$pinned_proto http;
access_log /var/log/nginx/access.log main;
access_log $mismatch combined if=\$xfp_https;
EOF
        (
            warned=
            while sleep 10; do
                [ -s "$mismatch" ] || continue
                [ -n "$warned" ] || echo "$0: APP_EXTERNAL_BASE_URL is http:// but requests arrive with X-Forwarded-Proto https, links stay http://. Set APP_EXTERNAL_BASE_URL to the https:// address or unset it." >&2
                warned=1
                : > "$mismatch"
            done
        ) &
        ;;
    *) : > "$out" ;;
esac
