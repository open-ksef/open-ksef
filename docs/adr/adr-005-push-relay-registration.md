# ADR-005: Push relay registration stays open

Status: accepted (2026-11-14, T-039).

## Context

- Self-hosted instances register with the team relay from the setup wizard and the admin panel, with no account and no prior contact (`Api/Services/SystemSetupService.cs`, `SystemSettingsService.cs`). Many run behind NAT or on a LAN.
- `POST /api/register` takes any `instanceUrl` and returns an instance id and an HMAC key. Re-registration with an HMAC proof disables the old instance (#140).
- Before T-039 the relay saw only the reverse proxy IP (Cloudflare -> NPM -> container), so the 5/h per-IP register limit was one global bucket. The push limit was keyed on the unauthenticated `X-Relay-Instance` header, so a spoofed header could drain a real instance's quota.

## Decision

Option A: registration stays open. Abuse is bounded instead of gated:

1. The register limit (5/h per IP) uses the real client IP from `CF-Connecting-IP`, trusted only from `ForwardedHeaders:KnownNetworks`.
2. Pushes are limited to 60/min per verified instance. The HMAC is checked before the limit and before the body is parsed, so unsigned traffic costs no instance quota. A per-IP global limit (600/min) protects the HMAC and DB work.
3. The admin can disable an instance (`POST /api/admin/instances/{id}/disable`).
4. An instance can only push to device tokens its own users registered, and the app user picks the server. A rogue instance reaches no one who did not choose it.

## Consequences

- Anyone can obtain a relay key. A flood of registrations from many IPs creates rows, not pushes; cleanup is manual through the admin endpoints.
- Origin hardening is an ops duty: the proxy must accept only Cloudflare IPs or overwrite `CF-Connecting-IP` (`docs/push-relay-infra-setup.md`).
- No change for self-hosters or released instances.

## Rejected

- B, callback proof (relay calls `instanceUrl` back): breaks NAT and LAN instances, which are the common self-hosted case.
- C, invite code, and D, pending admin approval: change the self-hoster onboarding and need a CEO decision. Out of scope; a new task if wanted.
