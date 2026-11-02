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
- Never paste a KSeF token into a ticket or a log.

## Find the invoice

Read-only:

```sql
SELECT "Id", "TenantId", "Status", "Number", "IssuedAt", "AttemptCount", "NextAttemptAt",
       "KsefSessionRef", "KsefElementRef", "KsefNumber", "LastError", "EscalatedAt", "KsefEnvironment"
FROM "IssuedInvoices"
WHERE "EscalatedAt" IS NOT NULL
ORDER BY "IssuedAt";
```

Confirm the worker runs and is polling: the Worker log has `IssuedInvoiceSendService started` and no repeating `Unhandled error in issued invoice send cycle`.

## Resolve by `LastError`

| `LastError` | Meaning | Action |
|---|---|---|
| `KSeF nie odpowiada, ponowimy wysyłkę` | Network error or KSeF 5xx | Check the KSeF status pages. No action; retries recover it |
| `KSeF ogranicza liczbę żądań, ponowimy wysyłkę` | 429 | No action unless it lasts: look for another client using the same NIP |
| `KSeF odrzucił token...` | Token invalid or revoked | Owner generates a new token and replaces it in company settings. The next cycle recovers the invoice without a new POST if the element is known |
| `Token KSeF nie ma uprawnienia...` | Token has no write permission | As above, with write permission |
| `Wysyłka faktur wymaga tokenu KSeF...` | Token credential removed | Owner adds a token |
| `Środowisko KSeF zmienione` | The invoice was sent to another environment than the current one; re-checked daily | Set the environment back, or resolve it manually in KSeF (below). It cannot resolve while the environment differs |
| `Nie można jeszcze potwierdzić wyniku wysyłki w KSeF...` | More than one POST, and either an unresolved 440 or a failure status | See the next two sections |
| `UPO jeszcze niedostępne, ponowimy pobranie` | Accepted, no UPO yet | Usually KSeF lag. See "Missing UPO" |

## Failure status after a resend

`AttemptCount` above 1 and a failure status other than 430, 440 or 450: the worker posts the stored bytes again in a new session, after 15 minutes, doubling up to 4 hours. Check the status of the current element:

```
GET /v2/sessions/{KsefSessionRef}/invoices/{KsefElementRef}
```

| Code | Why it repeats | Who fixes it |
|---|---|---|
| 405 session error, 435 decrypt error, 500, 550, unknown 5xx | KSeF side, tied to one session; a new session may pass | KSeF. If it lasts more than a day, open a ticket with the Ministry with the session and element refs |
| 410 permission scope | Token | Owner replaces the token; the next post proves the result |
| 415 attachments not enabled | KSeF account setting. Our FA(3) writer sends no attachment, so this one is unexpected | Check the stored XML (`SentXml`), then the account setting or a ticket |
| unknown 4xx | Not documented when written | Read the code in `ksef-docs/open-api.json`, then open a dev ticket |

A new post that returns 200 accepts the invoice. A new post that returns 440 with a hash match accepts it with the original KSeF number. No step is needed from support in either case.

## Unresolved 440

KSeF says the seller NIP, invoice type and number exist, and the original was not listed with our hash. The element's status holds `originalKsefNumber` and `originalSessionReferenceNumber`.

1. Look up the original in KSeF by `originalKsefNumber`, or list `GET /v2/sessions/{originalSessionReferenceNumber}/invoices`.
2. Compare its `invoiceHash` with the base64 SHA-256 of `SentXml`.
3. Listed with our hash: the next cycle accepts it with no action. Listed with another hash: the worker rejects it as number taken, with the KSeF number shown.
4. Listing returns 403 or keeps failing: usually a token of another context or scope. Have the owner check the token, then re-check.

## Missing UPO

The invoice is legally in KSeF: it has `KsefNumber` and the KOD I link works.

1. Check `GET /v2/sessions/{KsefSessionRef}/invoices/{KsefElementRef}/upo`. A 404 or 5xx means KSeF has not generated it.
2. If it stays missing for days, open a ticket with the Ministry with the KSeF number. The owner can keep working, and the worker keeps fetching.

## After resolving

The flag clears when UPO arrives or the invoice is rejected. If a case needs a manual database change, stop and escalate to the principal engineer with a backup first. This runbook authorizes none.
