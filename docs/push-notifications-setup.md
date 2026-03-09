# Push Notifications Setup

Push notifications let the mobile app receive real-time alerts when new invoices arrive from KSeF. OpenKSeF uses a **layered delivery architecture** inspired by Home Assistant's Companion App model:

| Layer | Method | When it works | Setup needed |
|-------|--------|---------------|--------------|
| **1. SignalR (local push)** | Direct WebSocket connection | App is connected to the API | None (always on) |
| **2. Relay (team-operated)** | HTTP POST to relay server | App is in background, any network | Toggle ON in admin wizard (default) |
| **3. Direct FCM/APNs** | Firebase / Apple push | App is in background, own Firebase | Advanced: paste Firebase JSON |
| **4. Email fallback** | SMTP | Always | Configure SMTP in admin wizard |

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
    └─ 4. Email: send notification email to tenant email address
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

**Setup:** In the admin wizard (Step 5 - Integrations), select "Relay OpenKSeF" (it's the default). The relay URL is pre-filled. Optionally, enter an API key if provided by the team.

**Security:** Requests to the relay are signed with an HMAC (using the relay API key) to prevent unauthorized senders.

### Layer 3: Direct Firebase / APNs (advanced)

For admins who want full control over push delivery, the existing direct FCM/APNs path is preserved. This requires creating your own Firebase project and managing credentials.

See the [Firebase setup section](#android--firebase-cloud-messaging-fcm) below.

### Layer 4: Email fallback

If a tenant has a notification email configured, the system always sends an email notification regardless of push delivery success.

---

## Admin Wizard Configuration

In the admin setup wizard at `http://localhost:8080/admin-setup`, Step 5 (Integrations) provides three push notification modes:

### Option A: Relay (default, recommended)

- Select "Relay OpenKSeF"
- The URL `https://push.open-ksef.pl` is pre-filled
- Optionally enter an API key
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
2. Navigate to **Urządzenia** (Devices)
3. Find the registered device in the table
4. Click **Testuj** — the API sends a test push and reports success/failure

### Automatic confirmation on registration

Every time a device registers via `POST /api/devices/register`, the API automatically sends a confirmation push. If the push fails (e.g. relay not configured), the registration still succeeds.

---

## Relay Service Deployment (team-operated)

The relay service is at `src/OpenKSeF.PushRelay/`. It's a standalone ASP.NET Minimal API that receives push requests and forwards them to FCM/APNs.

### Running locally

```bash
cd src/OpenKSeF.PushRelay
dotnet run
```

### Docker

```bash
docker build -t openksef-push-relay -f src/OpenKSeF.PushRelay/Dockerfile src/OpenKSeF.PushRelay/
docker run -p 8084:8080 \
  -e Firebase__CredentialsJson='{"type":"service_account",...}' \
  -e Relay__ApiKey='your-shared-key' \
  openksef-push-relay
```

### Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `Firebase__CredentialsJson` | For Android push | *(none)* | Firebase service account JSON |
| `Relay__ApiKey` | Recommended | *(none)* | HMAC key for authenticating requests |
| `APNs__BundleId` | For iOS push | `com.openksef.mobile` | iOS app bundle ID |
| `APNs__BaseUrl` | For iOS push | `https://api.push.apple.com` | APNs endpoint |
| `APNs__KeyId` | For iOS push | *(none)* | APNs Auth Key ID |
| `APNs__TeamId` | For iOS push | *(none)* | Apple Developer Team ID |
| `APNs__AuthKeyP8` | For iOS push | *(none)* | `.p8` private key content |

### API

```
POST /api/push
{
  "pushToken": "device-fcm-token",
  "title": "New invoice",
  "body": "Invoice from ...",
  "data": { "tenantId": "...", "invoiceId": "..." }
}

Headers:
  X-Relay-Timestamp: <unix-epoch-seconds>
  X-Relay-Signature: <hmac-sha256-hex>
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| No notifications at all | No push providers configured | Enable relay in admin wizard or configure Firebase |
| SignalR not connecting | Auth token expired or wrong server URL | Re-login in the mobile app |
| Relay returns 401 | Invalid HMAC signature | Check that API key matches between instance and relay |
| Relay returns 502 | Firebase/APNs credentials invalid on relay | Check relay logs, verify Firebase JSON |
| FCM token invalid | Device token expired or wrong Firebase project | Re-register device; check `google-services.json` matches |
| iOS push returns 403 | Missing APNs JWT auth | Use relay (handles APNs auth) or implement JWT in `ApnsPushProvider` |

---

## Environment variable reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `FIREBASE_CREDENTIALS_JSON` | For direct FCM | *(none — uses relay)* | Firebase service account JSON (single line) |
| `APNS_BUNDLE_ID` | For direct iOS | `com.openksef.mobile` | iOS app bundle identifier |
| `APNS_BASE_URL` | For direct iOS | `https://api.push.apple.com` | APNs endpoint |
| `APNS_KEY_ID` | For direct iOS | *(none)* | APNs Auth Key ID |
| `APNS_TEAM_ID` | For direct iOS | *(none)* | Apple Developer Team ID |
| `APNS_AUTH_KEY_P8` | For direct iOS | *(none)* | Contents of the `.p8` private key file |
