# PushRelay Infrastructure Setup

Deploy the push relay behind Cloudflare on a separate server.

```
OpenKSeF instances --> Cloudflare (HTTPS + rate limit) --> PushRelay container --> FCM / APNs
```

## Prerequisites

- Server with Docker
- Domain (e.g. `push.open-ksef.pl`) pointed to the server via Cloudflare (proxied)
- Firebase service account JSON (for Android push)

## 1. Deploy

Copy `docker-compose.push-relay.yml` and create `.env`:

```bash
PUSH_RELAY_IMAGE=ghcr.io/open-ksef/openksef-push-relay:latest
PUSH_RELAY_API_KEY=<openssl rand -hex 32>
FIREBASE_CREDENTIALS_JSON={"type":"service_account",...}
# Optional (iOS):
# APNS_KEY_ID=
# APNS_TEAM_ID=
# APNS_AUTH_KEY_P8=
```

```bash
docker compose -f docker-compose.push-relay.yml up -d
curl http://localhost:8084/health  # {"status":"healthy"}
```

## 2. Cloudflare

1. **DNS:** A record `push` → server IP (proxied)
2. **SSL:** Full (strict)
3. **WAF rate limit:** `/api/push`, 100 req/min per IP, block
4. **Bot Fight Mode:** ON

## 3. Configure OpenKSeF instances

In admin wizard (Step 5) or Settings > Integrations:
- Select **Relay OpenKSeF**
- URL: `https://push.open-ksef.pl`
- API key: same value as `PUSH_RELAY_API_KEY`

## Security checklist

- [ ] Strong random `PUSH_RELAY_API_KEY` (same on relay + all instances)
- [ ] Cloudflare SSL Full (strict)
- [ ] Rate limiting on `/api/push`
- [ ] Firebase JSON not exposed publicly
- [ ] Firewall: only 80/443 open
