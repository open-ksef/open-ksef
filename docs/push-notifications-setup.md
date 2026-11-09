# Push Notifications Setup

Push notifications let the mobile app receive real-time alerts when new invoices arrive from KSeF. OpenKSeF uses a **layered delivery architecture** inspired by Home Assistant's Companion App model:

| Layer | Method | When it works | Setup needed |
|-------|--------|---------------|--------------|
| **1. SignalR (local push)** | Direct WebSocket connection | App is connected to the API | None (always on) |
| **2. Relay (team-operated)** | HTTP POST to relay server | App is in background, any network | Toggle ON in admin wizard (default) |
| **3. Direct FCM/APNs** | Firebase / Apple push | App is in background, own Firebase | Advanced: paste Firebase JSON |
| **4. Email** | SMTP | Every new invoice, API and Worker, with or without devices | Off by default: set `EMAIL_*` in `.env`, see [Layer 4](#layer-4-email) |

Most self-hosted admins only need **Layer 1 + Layer 2** which require no Firebase setup at all.

---

## How it works

```
New invoice synced from KSeF
    │
    ├─ 1. SignalR: send to connected mobile clients via WebSocket (instant, local)
    │
    ├─ 2. Relay: POST to push.open-ksef.pl which forwards to FCM/APNs
    │      (the relay owns the Firebase/APNs credentials)
    │
    ├─ 3. Direct FCM/APNs: if own Firebase configured, send directly
    │
    └─ 4. Email: to the company notification address, when enabled (independent of 1-3)
```

### Layer 1: SignalR (local push)

The MAUI mobile app maintains a SignalR (WebSocket) connection to the API at `/hubs/notifications`. When a new invoice arrives, the API sends a message directly to all connected clients for the user. The app displays a local notification.

- Works on both Android and iOS
- No cloud services, no rate limits
- Requires the app to be running and connected
- Connection is established after login and maintained with automatic reconnect

### Layer 2: Relay (recommended for remote push)

The OpenKSeF team operates a lightweight relay service at `https://push.open-ksef.pl`. Self-hosted instances POST notification payloads to this relay, which then forwards them to the mobile device via Firebase Cloud Messaging (Android) or Apple Push Notification service (iOS).

**Why this works:** The official OpenKSeF mobile app is built with the team's Firebase project (`google-services.json`). The relay service holds the corresponding Firebase server credentials. Self-hosted admins never need to touch Firebase.

**Security:** Each instance registers with the relay during the admin setup wizard and receives a unique HMAC API key. Requests are signed with this key, timestamps are validated (5-minute window), and per-instance rate limits are enforced. The relay runs behind Cloudflare with additional WAF protection.

**Setup:** In the admin wizard (Step 5 - Integrations), select "Relay OpenKSeF" (it's the default). The relay URL is pre-filled. Registration happens automatically when you complete the wizard — no manual key management needed.

### Layer 3: Direct Firebase / APNs (advanced)

For admins who want full control over push delivery, the existing direct FCM/APNs path is preserved. This requires creating your own Firebase project and managing credentials.

See the [Firebase setup section](#android--firebase-cloud-messaging-fcm) below.

### Layer 4: Email

An independent channel, not a fallback. One e-mail per new purchase invoice goes to the company notification address, from the Worker (scheduled sync) and the API (manual sync), whether or not the owner has a device. First sync and full resync send none. SMTP from the wizard configures Keycloak only and has no effect here.

Set in `.env`; `docker-compose.yml` passes them to `api` and `worker` as `Email__*`:

| Variable | Default | Meaning |
|----------|---------|---------|
| `EMAIL_ENABLED` | `false` | `true` turns e-mail on |
| `EMAIL_SMTP_HOST` | empty | SMTP server |
| `EMAIL_SMTP_PORT` | `587` | `587` STARTTLS, `465` SSL on connect |
| `EMAIL_USE_SSL` | `true` | `false`: plain SMTP, no TLS |
| `EMAIL_USERNAME`, `EMAIL_PASSWORD` | empty | SMTP login; no login when the username is empty |
| `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | `noreply@openksef.local`, `OpenKSeF` | Sender |

Message: subject `Nowa faktura od {sprzedawca}`, body with seller, invoice number and amount (pl-PL), footer `Wysłane przez OpenKSeF`. A failed send is logged as one warning and not retried; each attempt gives up after 10 s. The invoice is stored before the send, so an SMTP outage never loses data or fails the sync.

---

## Admin Wizard Configuration

In the admin setup wizard at `http://localhost:8080/admin-setup`, Step 5 (Integrations) provides three push notification modes:

### Option A: Relay (default, recommended)

- Select "Relay OpenKSeF"
- The URL `https://push.open-ksef.pl` is pre-filled
- Registration happens automatically — a unique API key is generated and stored
- Done — no Firebase setup needed

### Option B: Own Firebase project (advanced)

- Select "Własny projekt Firebase"
- Paste your Firebase service account JSON
- See detailed Firebase setup below

### Option C: Local only (SignalR)

- Select "Tylko lokalne (SignalR)"
- No remote push notifications
- Users only receive notifications when the app is actively connected to the server

---

## Android — Firebase Cloud Messaging (FCM)

Only needed if you choose **Option B** (own Firebase project).

### Step 1: Create a Firebase project

1. Open [Firebase Console](https://console.firebase.google.com/)
2. Click **Add project** (or select an existing project)
3. Follow the wizard — you can disable Google Analytics if not needed

### Step 2: Register the Android app in Firebase

1. In the Firebase project, click **Add app > Android**
2. Enter the package name: `com.openksef.mobile`
3. (Optional) Enter an app nickname and debug signing certificate SHA-1
4. Click **Register app**
5. Download `google-services.json`

### Step 3: Add `google-services.json` to the mobile project

Place the downloaded file at:

```
src/OpenKSeF.Mobile/Platforms/Android/google-services.json
```

The `.csproj` auto-detects this file and enables `FIREBASE_ENABLED`, which compiles in the `PushNotificationFirebaseService` that handles token registration and incoming messages. Without this file the service is excluded from the build.

> **Do not commit `google-services.json` to version control.** It contains API keys specific to your Firebase project.

### Step 4: Generate a Firebase service account key (server-side)

1. In Firebase Console, go to **Project Settings > Service Accounts**
2. Select **Firebase Admin SDK** and click **Generate new private key**
3. A JSON file downloads — this is your service account credential

### Step 5: Configure via admin wizard or environment

**Via wizard:** Paste the JSON in Step 5 of the admin wizard under "Własny projekt Firebase".

**Via environment:** Flatten the JSON to a single line and set it in `.env`:

```
FIREBASE_CREDENTIALS_JSON={"type":"service_account","project_id":"your-project",...}
```

The wizard value wins over `FIREBASE_CREDENTIALS_JSON`. API and Worker read it at the first Android push. A changed JSON takes effect after a restart of both.

---

## iOS — Apple Push Notification service (APNs)

> **Note:** The current `ApnsPushProvider` sends HTTP/2 requests to APNs but does not include authentication headers. For iOS push, use the relay service (which handles APNs auth) or wait for the JWT implementation.

When using the relay service, iOS push notifications work out of the box since the relay manages APNs credentials.

---

## Verifying push notifications work

### SignalR (local push)

1. Log in to the mobile app — the SignalR connection starts automatically
2. In the Account page, notification status should show "SignalR połączony"
3. Trigger a KSeF sync — you should receive an instant notification via SignalR

### Remote push (relay or direct)

1. Open http://localhost:8080 and log in to the portal
2. Go to **Telefon**
3. Find the device under **Moje urządzenia**
4. Click **Wyślij test** — the API sends a test push and reports success/failure

### Automatic confirmation on registration

Every time a device registers via `POST /api/devices/register`, the API automatically sends a confirmation push. If the push fails (e.g. relay not configured), the registration still succeeds.

---

## Device Token Behavior

When the mobile app registers a device, it sends the platform push token (FCM for Android, APNs for iOS). If Firebase/APNs is not available (e.g. no `google-services.json` in the build), the app sends a placeholder token in the format `device-<guid>`.

A push token belongs to one account. When another account registers the same token, the device moves to that account. Deleting a company deletes the device subscriptions bound to it.

Devices with `device-*` tokens **only receive SignalR real-time notifications** while the app is connected. Remote push (relay, FCM, APNs) will fail silently for these tokens. This is expected behavior for development builds without Firebase configured.

---

## Background Sync Notifications (Worker)

The Worker process handles background KSeF synchronization. When new invoices are discovered during sync, the Worker sends push notifications:

- **Relay** → FCM → APNs (no SignalR, since the Worker has no hub connection)
- Email, when `EMAIL_ENABLED=true` (see [Layer 4](#layer-4-email))

The Worker reads push configuration from the same database (`SystemConfig` table) and supports the same `Firebase__CredentialsJson` environment variable as the API.

---

## Relay Service Deployment (team-operated)

The relay service is at `src/OpenKSeF.PushRelay/`. It's a standalone ASP.NET Minimal API that receives push requests and forwards them to FCM/APNs.

### Running locally

```bash
cd src/OpenKSeF.PushRelay
dotnet run
```

### Docker Compose

The relay has its own dedicated compose file (`docker-compose.push-relay.yml`), separate from the main Open-KSeF stack. It is deployed independently -- typically on a different server behind Cloudflare.

```bash
# Dev (build from source):
docker compose -f docker-compose.push-relay.yml up -d --build

# With an image you built and pushed yourself (the release workflow publishes no relay image):
PUSH_RELAY_IMAGE=<your-image> docker compose -f docker-compose.push-relay.yml up -d --no-build
```

### Production: Cloudflare + Docker

See [push-relay-infra-setup.md](push-relay-infra-setup.md) for the full production deployment guide.

### Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `Firebase__CredentialsJson` | For Android push | *(none)* | Firebase service account JSON |
| `Relay__ApiKey` | No | *(none)* | Legacy global HMAC key (for backward compat) |
| `Relay__AdminKey` | Recommended | *(none)* | Key protecting admin management endpoints |
| `Relay__DataDir` | No | `./data` | Directory for SQLite instance registry |
| `APNs__BundleId` | For iOS push | `com.openksef.mobile` | iOS app bundle ID |
| `APNs__BaseUrl` | For iOS push | `https://api.push.apple.com` | APNs endpoint |
| `APNs__KeyId` | For iOS push | *(none)* | APNs Auth Key ID |
| `APNs__TeamId` | For iOS push | *(none)* | Apple Developer Team ID |
| `APNs__AuthKeyP8` | For iOS push | *(none)* | `.p8` private key content |

### API

#### Registration (called by instances during setup)

```
POST /api/register
{
  "instanceUrl": "https://my-openksef.example.com"
}

Response:
{
  "instanceId": "abc123...",
  "apiKey": "64-hex-chars..."
}
```

#### Push (called by instances to send notifications)

```
POST /api/push
{
  "pushToken": "device-fcm-token",
  "title": "New invoice",
  "body": "Invoice from ...",
  "data": { "tenantId": "...", "invoiceId": "..." }
}

Headers:
  X-Relay-Instance: <instanceId>
  X-Relay-Timestamp: <unix-epoch-seconds>
  X-Relay-Signature: <hmac-sha256-hex>
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| No notifications at all | No push providers configured | Enable relay in admin wizard or configure Firebase |
| SignalR not connecting | Auth token expired or wrong server URL | Re-login in the mobile app |
| Relay returns 401 | Invalid HMAC signature or unregistered instance | **Ustawienia** > **Administracja instancji** > **Powiadomienia push**: **Zarejestruj ponownie** |
| Relay returns 403 | Instance has been disabled on the relay | Contact OpenKSeF team |
| Relay returns 429 | Rate limit exceeded | Reduce push frequency or contact OpenKSeF team |
| Relay returns 502 | Firebase/APNs credentials invalid on relay | Check relay logs, verify Firebase JSON |
| FCM token invalid | Device token expired or wrong Firebase project | Re-register device; check `google-services.json` matches |
| iOS push returns 403 | Missing APNs JWT auth | Use relay (handles APNs auth) or implement JWT in `ApnsPushProvider` |

---

## Environment variable reference

### Open-KSeF instance (API + Worker)

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `FIREBASE_CREDENTIALS_JSON` | For direct FCM | *(none — uses relay)* | Firebase service account JSON (single line). Used by both API and Worker. |

Direct APNs is not usable from an instance: `ApnsPushProvider` sends no authentication, and the shipped `docker-compose.yml` passes no APNs variables. The relay holds the APNs credentials.

### PushRelay service

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `PUSH_RELAY_ADMIN_KEY` | Recommended | *(none)* | Key protecting relay admin endpoints |
| `PUSH_RELAY_API_KEY` | No | *(none)* | Legacy global HMAC key (backward compat) |
| `PUSH_RELAY_HOST_PORT` | No | `8084` | Host port for the relay container |
| `FIREBASE_CREDENTIALS_JSON` | For Android push | *(none)* | Firebase service account JSON |
| `APNS_BUNDLE_ID` | For iOS push | `com.openksef.mobile` | iOS app bundle identifier |
| `APNS_BASE_URL` | For iOS push | `https://api.push.apple.com` | APNs endpoint |
| `APNS_KEY_ID` | For iOS push | *(none)* | APNs Auth Key ID |
| `APNS_TEAM_ID` | For iOS push | *(none)* | Apple Developer Team ID |
| `APNS_AUTH_KEY_P8` | For iOS push | *(none)* | Contents of the `.p8` private key file |
