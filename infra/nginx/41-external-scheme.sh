#!/bin/sh
set -eu
out=/etc/nginx/external-scheme.conf
case "${APP_EXTERNAL_BASE_URL:-}" in
    https://*) echo 'set $pinned_proto https;' > "$out" ;;
    http://*) echo 'set $pinned_proto http;' > "$out" ;;
    *) : > "$out" ;;
esac
