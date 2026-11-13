# Runbook: stuck issued invoice

An invoice is flagged when it is Queued, Sending or Accepted without UPO `Issuing:StuckAfterHours` (default 24) after `IssuedAt`. Then:

- Worker log at Error, once per window: `Issued invoice {InvoiceId} of tenant {TenantId} needs attention: status ..., attempts ..., session ..., element ..., last error ...`.
- One push to the tenant owner's registered devices. A portal-only owner gets no push, only the flag.
- `needsAttention: true` in the issued list. `IssuedInvoices.EscalatedAt` holds the last log time.

The worker keeps retrying. Escalation changes no status.

## Rules

- Never set `Status` by hand, and never clear `SendStartedAt`, `KsefSessionRef` or `AttemptCount`. They are the guards against a second tax document.
- Never advise a new invoice for the same sale while the invoice is Sending. KSeF may already hold the first copy, and a new number would make a second document. Copy returns 409 in that state on purpose.
- Copy of an Accepted invoice stays allowed: it has a KSeF number, only the UPO is missing. Issuing it for the same sale is still a second document.
- Never paste a KSeF token or an access token into a ticket or a log.
- Never decrypt `KSeFCredentials.EncryptedToken` from the database, and never reuse the worker's token for support calls.

## Find the invoice

Read-only:

```sql
SELECT "Id", "TenantId", "Status", "Number", "IssuedAt", "AttemptCount", "NextAttemptAt",
       "KsefSessionRef", "KsefElementRef", "KsefNumber", "LastError", "EscalatedAt", "KsefEnvironment"
FROM "IssuedInvoices"
WHERE "EscalatedAt" IS NOT NULL
ORDER BY "IssuedAt";
```

NIP and owner of the tenant. `UserId` is the Keycloak user:

```sql
SELECT "Nip", "UserId" FROM "Tenants" WHERE "Id" = '<tenant id>';
```

Confirm the worker runs and is polling: the Worker log has `IssuedInvoiceSendService started` and no repeating `Unhandled error in issued invoice send cycle` or `Issued invoice send for tenant {TenantId} failed`. After the second one the invoice row does not change, so `LastError` can be old.

## Reading from KSeF

Support has no KSeF access of its own. Every `/sessions` and `/invoices` call below needs an access token for the tenant's NIP. There are two ways to get an answer.

**Owner, no API.** The owner logs in to [Aplikacja Podatnika KSeF](https://ksef.podatki.gov.pl/aplikacja-podatnika-ksef-20/) in the environment of the invoice and searches issued invoices by `KsefNumber`, otherwise by `Number`. This shows whether the document exists and what it contains. It does not give the refs or status codes used below.

**Developer, API, token from the owner.** The owner generates a separate KSeF token in that application with `Introspection` for `/sessions/...`, plus `InvoiceRead` for `/invoices/ksef/...`, never `InvoiceWrite`, and hands it to the developer outside tickets and logs. The developer gets an access token, the same flow as `KSeFInvoiceSubmitter.LoginAsync`:

1. `POST {base}/auth/challenge` returns `challenge` and `timestampMs`.
2. `GET {base}/security/public-key-certificates`: take the certificate with usage `KsefTokenEncryption`. Encrypt `{token}|{timestampMs}` with RSA-OAEP SHA-256 (MGF1), then base64.
3. `POST {base}/auth/ksef-token` with `challenge`, `contextIdentifier` (`Nip`, the tenant's NIP) and `encryptedToken`. It returns `referenceNumber` and `authenticationToken.token`.
4. `GET {base}/auth/{referenceNumber}` with `Authorization: Bearer {authenticationToken}`, until `status.code` is 200.
5. `POST {base}/auth/token/redeem` with the same header returns `accessToken.token`.

Send `Authorization: Bearer {accessToken}` on the calls below. When done, the owner revokes the token in the application.

`{base}` comes from `KsefEnvironment` on the invoice, not from the current environment:

| `KsefEnvironment` | `{base}` |
|---|---|
| `test` | `https://api-test.ksef.mf.gov.pl/v2` |
| `demo` | `https://api-demo.ksef.mf.gov.pl/v2` |
| `production` | `https://api.ksef.mf.gov.pl/v2` |

Our hash, to compare with `invoiceHash` in KSeF:

```sql
SELECT encode(sha256("SentXml"), 'base64') FROM "IssuedInvoices" WHERE "Id" = '<invoice id>';
```

## Resolve by `LastError`

| `LastError` | Meaning | Action |
|---|---|---|
| `KSeF nie odpowiada, ponowimy wysyłkę` | Network error or KSeF 5xx | Check the KSeF status pages. No action; retries recover it |
| `KSeF ogranicza liczbę żądań, ponowimy wysyłkę` | 429 | No action unless it lasts: look for another client using the same NIP |
| `KSeF odrzucił token...` | Token invalid or revoked | Owner generates a new token and replaces it in company settings. The next cycle recovers the invoice without a new POST if the element is known |
| `KSeF odrzucił certyfikat...` | Certificate invalid, expired, revoked or unreadable | Owner uploads a new KSeF certificate in company settings. Recovery as above |
| `Brak uprawnienia do wystawiania faktur w KSeF...` (older rows: `Token KSeF nie ma uprawnienia...`) | Token or certificate has no write permission | As above, with write permission |
| `Wysyłka faktur wymaga tokenu lub certyfikatu KSeF...` (older rows: `Wysyłka faktur wymaga tokenu KSeF...`) | Credential removed | Owner adds a token or certificate |
| `Środowisko KSeF zmienione` | The invoice was sent to another environment than the current one; re-checked daily | Admin setup refuses a `ksef_environment` change while any invoice exists. Check the invoice in KSeF using `{base}` of the invoice, then escalate to the principal engineer. It cannot resolve while the environment differs |
| `Nie można jeszcze potwierdzić wyniku wysyłki w KSeF...` | More than one POST, and either an unresolved 440 or a failure status | `KsefElementRef` empty: "Failure status after a resend". `KsefElementRef` set: "Unresolved 440", unless the next cycle has not polled the new post yet |
| `UPO jeszcze niedostępne, ponowimy pobranie` | Accepted, no UPO yet | Usually KSeF lag. See "Missing UPO" |
| Empty | No error to report. Queued with `AttemptCount` 0: never picked up. Sending: KSeF status 100 or 150, or a 404 on the element. Accepted: first UPO call not made yet | Queued or Accepted: check the worker, as above. Sending: read `GET {base}/sessions/{KsefSessionRef}/invoices/{KsefElementRef}`. If it stays in processing for more than a day, open a ticket with the Ministry with both refs |
| Any other text | Raw KSeF text from a 4xx on a status check, session list, UPO call, later post or login. Retried every 15 minutes | Status checks also log `KSeF status check for issued invoice {InvoiceId} failed: ...`. 403 or a permission text: owner checks the token, as above. Otherwise open a dev ticket with the text and both refs |

## Failure status after a resend

`AttemptCount` above 1, `KsefElementRef` empty: the worker got a failure status other than 430, 440 or 450, and it posts the stored bytes again in a new session, after 15 minutes, doubling up to 4 hours. Read the code from the Worker log:

```
Issued invoice {InvoiceId} failed with status {Code} ({Description}) in session {SessionRef}, element {ElementRef}; next post at {NextAttemptAt}
```

If the log is gone, list `GET {base}/sessions/{KsefSessionRef}/invoices`, follow `x-continuation-token`, find our hash in `invoiceHash` and read `status.code`. A new post replaces `KsefSessionRef`, so this shows the latest failure only.

`KSeF session {SessionRef} of issued invoice {InvoiceId} cannot be listed` means the list call returned 400 or 404, for example 21173 for a session that is gone. The worker posts anyway. No action.

| Code | Why it repeats | Who fixes it |
|---|---|---|
| 405 session error, 435 decrypt error, 500, 550, unknown 5xx | KSeF side, tied to one session; a new session may pass | KSeF. If it lasts more than a day, open a ticket with the Ministry with the session and element refs |
| 410 permission scope | Token | Owner replaces the token; the next post proves the result |
| 415 attachments not enabled | KSeF account setting. Our FA(3) writer sends no attachment, so this one is unexpected | Check the stored XML (`SentXml`), then the account setting or a ticket |
| unknown 4xx | Not documented when written | Read the code in `open-api.json` of `CIRFMF/ksef-docs`, then open a dev ticket |

A new post that returns 200 accepts the invoice. A new post that returns 440 with a hash match accepts it with the original KSeF number. No step is needed from support in either case.

## Unresolved 440

KSeF says the seller NIP, invoice type and number exist, and the original was not listed with our hash. `KsefElementRef` is set. The 440 details are not stored, so read them from KSeF.

1. `GET {base}/sessions/{KsefSessionRef}/invoices/{KsefElementRef}` returns `status.extensions.originalKsefNumber` and `status.extensions.originalSessionReferenceNumber`.
2. List `GET {base}/sessions/{originalSessionReferenceNumber}/invoices`, find `originalKsefNumber`, and compare its `invoiceHash` with our hash.
3. Listed with our hash: the next cycle accepts it with no action. Listed with another hash: the worker rejects it as number taken, with the KSeF number shown.
4. Listing returns 403 or keeps failing: usually a token of another context or scope. Have the owner check the token, then re-check.
5. Listing returns 400, or `originalSessionReferenceNumber` is missing: the worker cannot decide and keeps waiting, with no automatic exit. Compare the document instead: the owner opens `originalKsefNumber` in the application, or a developer downloads `GET {base}/invoices/ksef/{originalKsefNumber}` and compares it with `SentXml`. Send the result to the principal engineer. Change nothing in the database.

## Missing UPO

The invoice is legally in KSeF: it has `KsefNumber` and the KOD I link works. The worker asks for the UPO every 30 seconds.

1. Check `GET {base}/sessions/{KsefSessionRef}/invoices/{KsefElementRef}/upo`. A 200 with XML means the next cycle stores it. A 404 or 5xx means KSeF has not generated it. A 400 or 403 fails the worker's call too: see "Any other text" in the table above.
2. If it stays missing for days, open a ticket with the Ministry with the KSeF number. The owner can keep working, and the worker keeps fetching.

## After resolving

The flag clears when UPO arrives or the invoice is rejected. If a case needs a manual database change, stop and escalate to the principal engineer with a backup first. This runbook authorizes none.
