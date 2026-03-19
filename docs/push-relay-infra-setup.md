# PushRelay Infrastructure Setup

Step-by-step guide for deploying the OpenKSeF PushRelay service in production with Cloudflare as a protection layer.

## Prerequisites

- A server (VPS, cloud VM, or container platform) with Docker installed
- A domain pointing to the server (e.g. `push.open-ksef.pl`)
- A Cloudflare account with the domain added
- Firebase project with service account credentials (for Android push)
- (Optional) Apple Developer account with APNs key (for iOS push)

---

## Security Architecture

The relay uses three defense layers:

1. **Cloudflare (Layer 1):** TLS termination, rate limiting per IP, bot protection. Stops the vast majority of abuse before it reaches the relay.
2. **Instance Registration (Layer 2):** Each self-hosted OpenKSeF instance registers with the relay during the admin setup wizard and receives a unique 256-bit HMAC key. No shared secrets in source code.
3. **Request Validation (Layer 3):** Every push request requires a valid HMAC signature, a fresh timestamp (5-minute window), and an enabled instance ID. In-code rate limiting per instance catches abuse that slips past Cloudflare.

```
┌─────────────────────┐     ┌────────────────┐     ┌────────────────────┐
│ Open-KSeF Instance  │────>│   Cloudflare   │────>│ PushRelay Container│
│  (API or Worker)    │     │  HTTPS + WAF   │     │  (Docker + SQLite) │
│                     │     │  Rate Limiting  │     │                    │
│  POST /api/push     │     │  Bot Protection │     │  FCM ──> Android   │
│  + HMAC Signature   │     │                │     │  APNs ──> iOS      │
│  + Instance ID      │     │                │     │                    │
└─────────────────────┘     └────────────────┘     └────────────────────┘
```

---

## Step 1: Prepare Firebase Credentials

1. Go to [Firebase Console](https://console.firebase.google.com/) > your project > Project Settings > Service Accounts
2. Click **Generate new private key**
3. Save the JSON file — you'll need its contents as an environment variable

---

## Step 2: Prepare APNs Credentials (optional, for iOS)

1. Go to [Apple Developer](https://developer.apple.com/) > Certificates, Identifiers & Profiles > Keys
2. Create a new key with APNs enabled
3. Download the `.p8` file
4. Note down the Key ID and Team ID

---

## Step 3: Deploy the PushRelay Container

Create a `.env` file:

```bash
PUSH_RELAY_IMAGE=ghcr.io/open-ksef/openksef-push-relay:latest
PUSH_RELAY_API_KEY=                          # Legacy global key (optional, for backward compat)
PUSH_RELAY_ADMIN_KEY=your-admin-secret-key   # Required for admin endpoints
FIREBASE_CREDENTIALS_JSON={"type":"service_account","project_id":"...","private_key":"...","client_email":"..."}
APNS_KEY_ID=
APNS_TEAM_ID=
APNS_AUTH_KEY_P8=
```

Start:

```bash
docker compose -f docker-compose.push-relay.yml up -d
```

### Verify the relay is running

```bash
curl http://localhost:8080/health
# Expected: {"status":"healthy"}
```

---

## Step 4: Configure Cloudflare

### DNS

1. In Cloudflare dashboard, go to your domain's DNS settings
2. Add an **A record**: `push` → `<server-ip>` (proxied, orange cloud ON)
3. This makes `push.open-ksef.pl` point to your server through Cloudflare

### SSL/TLS

1. Go to **SSL/TLS** > **Overview**
2. Set encryption mode to **Full (strict)**
3. Under **Edge Certificates**, ensure "Always Use HTTPS" is ON

### Rate Limiting (WAF)

1. Go to **Security** > **WAF** > **Rate limiting rules**
2. Create rules:
   - **Push endpoint:** URI Path equals `/api/push`, Rate: 100 requests per 1 minute per IP, Action: Block
   - **Registration:** URI Path equals `/api/register`, Rate: 10 requests per 1 hour per IP, Action: Block

### Bot Protection

1. Go to **Security** > **Bots**
2. Enable **Bot Fight Mode** (free tier) or **Super Bot Fight Mode** (Pro+)

---

## Step 5: Configure Open-KSeF Instances to Use the Relay

Each self-hosted Open-KSeF instance registers automatically during the admin setup wizard.

### Via Admin Wizard (recommended)

1. Open your OpenKSeF portal at `http://your-instance:8080/admin-setup`
2. In Step 5 (Integrations), select **Relay OpenKSeF**
3. The URL `https://push.open-ksef.pl` is pre-filled
4. Complete the wizard — registration happens automatically
5. The instance receives a unique API key stored in the database

### Via Settings Page (re-registration)

1. Log in as admin to the portal
2. Go to **Settings** > **Integracje** > **Powiadomienia push**
3. Select **Relay OpenKSeF**
4. Click **Zarejestruj ponownie** (Re-register) to get a new key
5. Save

---

## Step 6: Test End-to-End

1. Register a device in the mobile app (open the app, log in, complete onboarding)
2. In the portal, go to **Urządzenia** (Devices)
3. Find the registered device and click **Testuj** (Test)
4. Check if the mobile device receives the push notification
5. Check relay logs: `docker logs openksef-push-relay`

---

## Admin API

The relay exposes admin endpoints protected by the `PUSH_RELAY_ADMIN_KEY`. Use these to manage instances.

### List registered instances

```bash
curl -H "X-Admin-Key: $PUSH_RELAY_ADMIN_KEY" https://push.open-ksef.pl/api/admin/instances
```

### Disable a rogue instance

```bash
curl -X POST -H "X-Admin-Key: $PUSH_RELAY_ADMIN_KEY" \
  https://push.open-ksef.pl/api/admin/instances/{instanceId}/disable
```

### Re-enable an instance

```bash
curl -X POST -H "X-Admin-Key: $PUSH_RELAY_ADMIN_KEY" \
  https://push.open-ksef.pl/api/admin/instances/{instanceId}/enable
```

---

## Monitoring

### Health Check

```bash
curl https://push.open-ksef.pl/health
```

Set up uptime monitoring (e.g. UptimeRobot, Cloudflare Health Checks) to ping `/health` every minute.

### Logs

```bash
docker logs -f openksef-push-relay
```

Key log messages:
- `Firebase initialized` — Firebase credentials are valid
- `FCM push sent for token ...` — successful Android push
- `APNs push sent for token ...` — successful iOS push
- `Invalid relay signature from instance ...` — wrong API key
- `Unknown instance ...` — unregistered instance
- `Disabled instance ...` — revoked instance
- `Stale timestamp ...` — replay attack or clock skew
- `All providers failed` — neither FCM nor APNs could deliver
- `New instance registered: ...` — new registration

### Cloudflare Analytics

Monitor request volume and blocked requests in Cloudflare dashboard > Analytics & Logs.

---

## Security Checklist

- [ ] `PUSH_RELAY_ADMIN_KEY` is set to a strong random string (e.g. `openssl rand -hex 32`)
- [ ] Cloudflare SSL is set to "Full (strict)"
- [ ] Rate limiting is active on `/api/push` and `/api/register`
- [ ] Firebase credentials JSON is not exposed in logs or public endpoints
- [ ] Container runs as non-root (default in .NET 8 containers)
- [ ] Server firewall allows only ports 80/443 (Cloudflare handles the rest)
- [ ] `push-relay-data` volume is persisted and backed up (contains instance registry)
- [ ] Bot Fight Mode is enabled in Cloudflare

---

## Data Persistence

The relay stores registered instances in a SQLite database at `/app/data/instances.db` inside the container. This is persisted via the `push-relay-data` Docker volume.

**Backup:** Regularly back up the volume. If the database is lost, all instances will need to re-register (via the Settings page in each instance's portal).

**Migration:** The database schema is auto-created on first start. No manual migration needed.
