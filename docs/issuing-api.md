# Invoice issuing API contract

Source of truth for portal (`src/OpenKSeF.Portal.Web`) and mobile (`open-ksef-mobile` `Models/*`). C# types: `src/OpenKSeF.Api/Models/{IssuedInvoiceModels,SellerProfileModels,NipLookupModels,ValidationEnvelope}.cs`. Decision record: [ADR-001](adr/adr-001-invoice-issuing.md).

Changes after the first commit go through the architect: `Api/Models` and this file change in one commit.

## Endpoints and payloads

JSON camelCase, dates `yyyy-MM-dd` (`DateOnly`), money as JSON numbers with 2 dp, quantity up to 6 dp, `unitNetPrice` up to 2 dp.

```
GET  /api/tenants/{tenantId}/seller-profile                 -> 200 SellerProfileResponse (defaults + isComplete=false if none) | 404 tenant
PUT  /api/tenants/{tenantId}/seller-profile                 SellerProfileRequest -> 200 SellerProfileResponse | 400 ValidationEnvelope | 409 (next number too low: ValidationEnvelope; concurrent write: { message })
GET  /api/nip-lookup/{nip}                                  -> 200 NipLookupResponse | 400 | 404 | 503
GET  /api/tenants/{tenantId}/issued-invoices?page=&pageSize=&status=&month=&q=   -> 200 PagedResult<IssuedInvoiceListItemResponse> | 400 ValidationEnvelope
POST /api/tenants/{tenantId}/issued-invoices                IssuedInvoiceDraftRequest -> 201 IssuedInvoiceResponse | 400 ValidationEnvelope
GET  /api/tenants/{tenantId}/issued-invoices/{id}           -> 200 IssuedInvoiceResponse
PUT  /api/tenants/{tenantId}/issued-invoices/{id}           IssuedInvoiceDraftRequest -> 200 | 400 ValidationEnvelope | 422 ValidationEnvelope (INV-VAL-022) | 409 (not Draft/Rejected)
DELETE /api/tenants/{tenantId}/issued-invoices/{id}         -> 204 | 409 (not Draft)
POST /api/tenants/{tenantId}/issued-invoices/{id}/issue     -> 202 IssuedInvoiceResponse (status Sending) | 200 if already Sending/Accepted | 422 ValidationEnvelope | 409 (number already used, concurrent change)
POST /api/tenants/{tenantId}/issued-invoices/{id}/copy      -> 201 IssuedInvoiceResponse (new Draft, same buyer+lines, dates = today, no number) | 409 (source Sending)
GET  /api/tenants/{tenantId}/issued-invoices/{id}/xml       -> 200 application/xml (stored bytes) | 409 unless Accepted
GET  /api/tenants/{tenantId}/issued-invoices/{id}/upo       -> 200 application/xml | 409 unless Accepted with UPO
DELETE /api/tenants/{id}                                    -> 204 | 409 (invoice Queued or Sending, or Accepted on production) | 404
```

Cross-tenant on any route returns 404. A 409 without a `ValidationEnvelope` has the body `{ "message": "..." }`.

List filters:
- `status`: `Draft`, `Sending` (internal Queued + Sending), `Accepted`, `Rejected`.
- `month`: `yyyy-MM`, by `issueDate`.
- `q`: case-insensitive substring of buyer name, buyer NIP or invoice number.
- Order: `issueDate` descending, then `createdAt` descending. `pageSize` is capped at 100.

The `ksef` block:
- `environment`: `test`, `demo` or `production`. The environment the invoice was sent to; while it is Draft or Rejected, the current one.
- `statusCode`, `statusMessage`, `statusDetails`: the KSeF result, filled by the send worker only. A rejection carries the KSeF code and text. A token without write permission gives code 403 and the message "Token KSeF nie ma uprawnienia do wystawiania faktur. Wygeneruj token z uprawnieniem do zapisu i podmień go w ustawieniach firmy."
- `lastError`: a retryable problem while the invoice stays Sending, or a missing UPO on an Accepted invoice.
- `verificationUrl`: the KOD I link, only when Accepted. Built at issue time by KSeF.Client `IVerificationLinkService` from seller NIP, `issueDate` and the SHA-256 of the stored XML.
- `hasUpo`: `upo` is downloadable.
- `duplicateKsefNumber`: only on Rejected with code 440, when `statusDetails` holds a KSeF number: the document that already uses this invoice number. `ksefNumber` stays null.

`xml` returns the exact bytes sent to KSeF. `xml` and `upo` return 409 with `{ "message": "..." }` when not available.

Draft dates are optional in the request and never null in a response: `issueDate` defaults to today (Europe/Warsaw), `saleDate` to `issueDate`, `dueDate` to `issueDate` + 14 days.

A draft or rejected invoice shows the current seller profile in `seller`; issuing stores a snapshot, and later responses show the snapshot. A rejected invoice keeps its number and cannot be deleted.

A rejected invoice may change `issueDate` only within the period of its number, or PUT returns 422 `INV-VAL-022` and changes nothing. The period is stored when the number is assigned (month with `{MM}`, otherwise year), so a later pattern change has no effect. A number without a date token has no such limit. A draft has no number, so its date is free. The way to another period is copy.

A rejected invoice with code 440 cannot be issued again: `issue` returns 422 `INV-VAL-023` and sends nothing, because KSeF holds that number for this seller and every resend returns 440. Copy stays available. A rejected invoice with any other code can be fixed and issued again under the same number.

The send worker rejects on 440 only when a second document is certain: after one POST no earlier copy of ours exists, and after more POSTs the original must be listed with another hash. After more than one POST, an unresolved 440 leaves the invoice in Sending with `lastError` and a re-check every 15 minutes; nothing is resent. Codes 430 and 450 stay final, because they judge the invoice content and would have failed the first copy too.

After more than one POST, any other failure status (405, 410, 415, 435, unknown 4xx, 500 and above) leaves the invoice in Sending with `lastError` and posts the stored XML bytes again in a new session, after 15 minutes, doubling up to 4 hours. KSeF keys duplicates on seller NIP, invoice type and number, so these bytes cannot create a second document: a first copy returns 440, and the hash match recovers its KSeF number. A new post returning 200 accepts the invoice. No code only waits. Codes 410 and 415 keep failing until the token or the KSeF account changes, and that part escalates to a person (below).

`needsAttention` in the list is true while the worker has flagged the invoice as stuck: Queued, Sending or Accepted without UPO, `Issuing:StuckAfterHours` (default 24) after `issuedAt`. It clears on UPO or rejection. The worker logs at Error once per window and pushes to the tenant owner once. The flag changes no status and no rule: copy of a Sending invoice stays 409, and copy of an Accepted invoice stays allowed. Support steps: [issuing-stuck-runbook.md](issuing-stuck-runbook.md).

Copy of an invoice in status Sending returns 409: the first document may still reach KSeF, so a copy could become a second tax document.

An issue date after today in Europe/Warsaw blocks issuing with `INV-VAL-020` before a number is assigned, because KSeF rejects it with code 450. Drafts may hold a future date.

Tenant delete returns 409 while any invoice of the tenant is Queued or Sending, on any environment, or Accepted on production. `KsefEnvironment` stored on the invoice decides, not the current instance setting, and an Accepted invoice without one counts as production. Otherwise the delete removes the tenant and all its issued invoices in one transaction, and they cannot be restored. The KSeF copy of an accepted invoice stays in KSeF. To show the count before confirming, read `totalCount` from `GET /api/tenants/{tenantId}/issued-invoices?pageSize=1`; it includes drafts. The delete locks the tenant and invoice rows, so it waits for a concurrent issue or status change and decides on the committed status. An issue that waits for a delete that commits returns 404. Deleting the last invoices on the instance releases the KSeF environment lock. Deleting one Rejected invoice stays forbidden.

Numbering patterns must contain `{RRRR}` (`SP-VAL-007`), because the counter resets every year and a number without the year would repeat. A profile stored before this rule keeps working: GET returns its pattern and issuing uses it, and a repeated number returns 409. Its next PUT must supply a pattern with `{RRRR}`.

```jsonc
// SellerProfileRequest
{ "legalName": "OpenKSeF Test A sp. z o.o.", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
  "bankAccount": "61109010140000071219812874", "cashAccounting": false,
  "numberingPattern": "FV/{nr}/{MM}/{RRRR}", "nextNumber": 57, "nextNumberPeriod": "2026-11" }   // last two optional
// SellerProfileResponse = request fields +
{ "nip": "1234567890", "isComplete": true, "nextNumberPreview": "FV/57/11/2026", "ksefEnvironment": "test" }  // test|demo|production

// NipLookupResponse
{ "nip": "1234567890", "name": "...", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
  "vatStatus": "Czynny", "bankAccounts": ["61109010140000071219812874"] }                          // Czynny|Zwolniony|Niezarejestrowany

// IssuedInvoiceDraftRequest (no seller fields; unknown fields ignored)
{ "buyer": { "kind": "Company", "nip": "1234567890", "name": "...", "addressLine1": "...", "addressLine2": "..." },  // kind Company|Person; Person: nip null
  "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
  "paymentMethod": "Transfer", "splitPayment": false,                                             // Transfer|Cash|Card
  "exemptionBasis": { "kind": "Act", "text": "art. 113 ust. 1 ustawy o VAT" },                     // Act|Directive|Other; null if no zw line
  "lines": [ { "name": "Usługa", "unit": "szt.", "quantity": 1, "unitNetPrice": 100.00, "vatRate": "23" } ] }  // 23|8|5|0 KR|zw

// IssuedInvoiceResponse
{ "id": "guid", "status": "Draft", "number": null,                                              // Draft|Sending|Accepted|Rejected
  "seller": { "nip": "...", "name": "...", "addressLine1": "...", "addressLine2": "...", "bankAccount": "...", "cashAccounting": false },
  "buyer": { "kind": "Company", "nip": "...", "name": "...", "addressLine1": "...", "addressLine2": "..." },
  "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
  "paymentMethod": "Transfer", "splitPayment": false, "exemptionBasis": null,
  "lines": [ { "lineNo": 1, "name": "Usługa", "unit": "szt.", "quantity": 1, "unitNetPrice": 100.00, "vatRate": "23", "netAmount": 100.00, "vatAmount": 23.00 } ],
  "vatSummary": [ { "vatRate": "23", "net": 100.00, "vat": 23.00 } ],
  "totalNet": 100.00, "totalVat": 23.00, "totalGross": 123.00, "currency": "PLN",
  "ksef": { "environment": "test", "ksefNumber": null, "acceptedAt": null, "statusCode": null, "statusMessage": null, "statusDetails": null,
            "lastError": null, "verificationUrl": null, "hasUpo": false, "duplicateKsefNumber": null },
  "warnings": [ { "code": "INV-W-MPP", "field": "splitPayment", "message": "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)" } ],
  "createdAt": "2026-11-03T10:00:00Z", "updatedAt": "2026-11-03T10:00:00Z", "issuedAt": null }

// IssuedInvoiceListItemResponse
{ "id": "guid", "status": "Accepted", "number": "FV/1/11/2026", "buyerName": "...", "buyerNip": "...",
  "issueDate": "2026-11-03", "dueDate": "2026-11-17", "totalNet": 100.00, "totalGross": 123.00, "currency": "PLN", "ksefNumber": "...", "needsAttention": false }

// ValidationEnvelope (400/422)
{ "errors": [ { "code": "INV-VAL-013", "field": "buyer.nip", "message": "Dla nabywcy firmy wymagany jest poprawny NIP" } ], "warnings": [] }
```

Totals rule for client previews (they must equal the server; shared vectors in `Domain.Tests/Invoicing/TotalsVectors.json`, copied to portal and mobile tests):
- `netAmount` = round(qty x unitNetPrice, 2).
- Per rate: `vat` = round(sum net x rate, 2), half away from zero.
- Line `vatAmount` is for display only.
- Vectors: 3 x 0,33 @23 -> 0,99 / 0,23 / 1,22; 100 @23 + 100 @8 -> 231,00; 50 @`0 KR` -> vat 0.


## Validation codes

Stable codes in `ValidationEnvelope.errors` (issue blocked) and `warnings` (never block). Source: `src/OpenKSeF.Domain/Invoicing/IssueChecks.cs`, `Domain/Services/IssuedInvoiceService.cs` (draft shape, returned as 400).

| Code | Field | Message |
|---|---|---|
| INV-VAL-002 | `lines` | Faktura musi zawierać co najmniej jedną pozycję |
| INV-VAL-003 | `lines` | Faktura może mieć najwyżej 1000 pozycji (draft 400) |
| INV-VAL-010 | `seller.name` | Uzupełnij nazwę firmy w danych do faktur |
| INV-VAL-011 | `seller.addressLine1` | Uzupełnij adres firmy w danych do faktur |
| INV-VAL-012 | `seller.bankAccount` | Uzupełnij numer rachunku w danych firmy (przelew only) |
| INV-VAL-013 | `buyer.nip` | Dla nabywcy firmy wymagany jest poprawny NIP |
| INV-VAL-014 | `buyer.nip` | Dla osoby prywatnej nie podawaj NIP |
| INV-VAL-015 | `buyer.name` | Uzupełnij nazwę nabywcy |
| INV-VAL-016 | `buyer.addressLine1` | Uzupełnij adres nabywcy |
| INV-VAL-020 | `issueDate` | Data wystawienia nie może być późniejsza niż dzisiejsza |
| INV-VAL-021 | `dueDate` | Termin płatności jest wcześniejszy niż data wystawienia |
| INV-VAL-022 | `issueDate` | Numer X należy do okresu 11/2026. Zostaw datę wystawienia w tym okresie albo użyj „Kopiuj jako nową”. Nowa faktura dostanie nowy numer, a ten pozostanie niewykorzystany. (rejected invoice PUT 422) |
| INV-VAL-023 | `number` | Numer X jest już w KSeF, więc nie można go wysłać ponownie. Numer KSeF: Y. Sprawdź tę fakturę w KSeF. Jeśli to ta sama sprzedaż, nie wystawiaj jej ponownie. (issue 422; the KSeF number sentence only when known) |
| INV-VAL-050 | `lines[i].name` | Każda pozycja musi mieć nazwę |
| INV-VAL-051 | `lines[i].quantity` | Ilość na pozycji musi być większa od zera |
| INV-VAL-052 | `lines[i].unitNetPrice` | Cena netto nie może być ujemna |
| INV-VAL-053 | `lines[i].quantity` | Ilość poza zakresem lub ponad 6 miejsc po przecinku (draft 400) |
| INV-VAL-054 | `lines[i].unitNetPrice` | Cena lub wartość pozycji poza zakresem, lub ponad 2 miejsca po przecinku (draft 400) |
| INV-VAL-060 | `lines[i].vatRate` | Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw |
| INV-VAL-061 | `exemptionBasis` | Podaj podstawę zwolnienia z VAT |
| INV-VAL-070 | `buyer.kind` | Rodzaj nabywcy: Company lub Person (draft 400) |
| INV-VAL-071 | `paymentMethod` | Forma płatności: Transfer, Cash lub Card (draft 400) |
| INV-VAL-072 | `exemptionBasis.kind` | Podstawa zwolnienia: Act, Directive lub Other (draft 400) |
| INV-VAL-073 | text fields | Pole może mieć najwyżej N znaków (draft 400; names and addresses 512, unit and exemption text 256) |
| INV-VAL-080 | `status` | Dozwolone statusy: Draft, Sending, Accepted, Rejected (list 400) |
| INV-VAL-081 | `month` | Miesiąc musi mieć format RRRR-MM (list 400) |
| INV-XSD | null | Faktura nie spełnia schematu FA(3): ... (issue 422, one per schema error) |
| INV-W-NIP | `buyer.nip` | NIP nabywcy ma niepoprawną sumę kontrolną (warning) |
| INV-W-MPP | `splitPayment` | Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15) (warning, gross > 15 000,00) |
| SP-VAL-001 | `bankAccount` | Niepoprawny numer rachunku (seller profile PUT) |
| SP-VAL-002 | `numberingPattern` | Wzorzec numeracji musi zawierać {nr} |
| SP-VAL-003 | `nextNumber` | Następny numer musi być większy od zera |
| SP-VAL-004 | `nextNumberPeriod` | Okres numeracji musi mieć format RRRR-MM (wzorzec z {MM}) lub RRRR |
| SP-VAL-005 | `legalName`, `addressLine1`, `addressLine2`, `numberingPattern` | Field too long (512; pattern 100) |
| SP-VAL-006 | `nextNumber` | Numer N jest już użyty. Ostatni numer w tym okresie to M, podaj co najmniej M+1. (seller profile PUT 409) |
| SP-VAL-007 | `numberingPattern` | Wzorzec numeracji musi zawierać {RRRR}, inaczej numery powtórzą się w kolejnym roku (seller profile PUT 400) |
| NIP-INVALID | `nip` | Niepoprawny NIP (nip-lookup 400) |
| NIP-NOT-FOUND | `nip` | Brak podmiotu w wykazie podatników VAT, wpisz dane ręcznie (nip-lookup 404) |
| NIP-LOOKUP-UNAVAILABLE | `nip` | Wyszukiwarka MF niedostępna, wpisz dane ręcznie (nip-lookup 503) |

NIP lookup: rate limited to 30 requests per hour per client IP (429 above it); results cached 24 h per NIP and day.
