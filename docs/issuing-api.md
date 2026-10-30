# Invoice issuing API contract

Source of truth for portal (`src/OpenKSeF.Portal.Web`) and mobile (`open-ksef-mobile` `Models/*`). C# types: `src/OpenKSeF.Api/Models/{IssuedInvoiceModels,SellerProfileModels,NipLookupModels,ValidationEnvelope}.cs`. Decision record: [ADR-001](adr/adr-001-invoice-issuing.md).

Changes after the first commit go through the architect: `Api/Models` and this file change in one commit.

## Endpoints and payloads

JSON camelCase, dates `yyyy-MM-dd` (`DateOnly`), money as JSON numbers with 2 dp, quantity up to 6 dp, `unitNetPrice` up to 2 dp.

```
GET  /api/tenants/{tenantId}/seller-profile                 -> 200 SellerProfileResponse (defaults + isComplete=false if none) | 404 tenant
PUT  /api/tenants/{tenantId}/seller-profile                 SellerProfileRequest -> 200 SellerProfileResponse | 400 ValidationEnvelope
GET  /api/nip-lookup/{nip}                                  -> 200 NipLookupResponse | 400 | 404 | 503
GET  /api/tenants/{tenantId}/issued-invoices?page=&pageSize=&status=   -> 200 PagedResult<IssuedInvoiceListItemResponse>
POST /api/tenants/{tenantId}/issued-invoices                IssuedInvoiceDraftRequest -> 201 IssuedInvoiceResponse
GET  /api/tenants/{tenantId}/issued-invoices/{id}           -> 200 IssuedInvoiceResponse
PUT  /api/tenants/{tenantId}/issued-invoices/{id}           IssuedInvoiceDraftRequest -> 200 | 409 (not Draft/Rejected)
DELETE /api/tenants/{tenantId}/issued-invoices/{id}         -> 204 | 409 (not Draft)
POST /api/tenants/{tenantId}/issued-invoices/{id}/issue     -> 202 IssuedInvoiceResponse (status Sending) | 200 if already Sending/Accepted | 422 ValidationEnvelope
POST /api/tenants/{tenantId}/issued-invoices/{id}/copy      -> 201 IssuedInvoiceResponse (new Draft, same buyer+lines, dates = today, no number)
GET  /api/tenants/{tenantId}/issued-invoices/{id}/xml       -> 200 application/xml (stored bytes) | 409 unless Accepted
GET  /api/tenants/{tenantId}/issued-invoices/{id}/upo       -> 200 application/xml | 409 unless Accepted with UPO
```

Cross-tenant on any route returns 404.

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
  "ksef": { "environment": "test", "ksefNumber": null, "acceptedAt": null, "statusCode": null, "statusMessage": null,
            "lastError": null, "verificationUrl": null, "hasUpo": false },
  "warnings": [ { "code": "INV-W-MPP", "field": "splitPayment", "message": "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)" } ],
  "createdAt": "2026-11-03T10:00:00Z", "updatedAt": "2026-11-03T10:00:00Z", "issuedAt": null }

// IssuedInvoiceListItemResponse
{ "id": "guid", "status": "Accepted", "number": "FV/1/11/2026", "buyerName": "...", "buyerNip": "...",
  "issueDate": "2026-11-03", "totalGross": 123.00, "currency": "PLN", "ksefNumber": "..." }

// ValidationEnvelope (400/422)
{ "errors": [ { "code": "INV-VAL-013", "field": "buyer.nip", "message": "Dla nabywcy firmy wymagany jest poprawny NIP" } ], "warnings": [] }
```

Totals rule for client previews (they must equal the server; shared vectors in `Domain.Tests/Invoicing/TotalsVectors.json`, copied to portal and mobile tests):
- `netAmount` = round(qty x unitNetPrice, 2).
- Per rate: `vat` = round(sum net x rate, 2), half away from zero.
- Line `vatAmount` is for display only.
- Vectors: 3 x 0,33 @23 -> 0,99 / 0,23 / 1,22; 100 @23 + 100 @8 -> 231,00; 50 @`0 KR` -> vat 0.


## Validation codes

Stable codes in `ValidationEnvelope.errors` (issue blocked) and `warnings` (never block). Source: `src/OpenKSeF.Domain/Invoicing/IssueChecks.cs`.

| Code | Field | Message |
|---|---|---|
| INV-VAL-002 | `lines` | Faktura musi zawierać co najmniej jedną pozycję |
| INV-VAL-010 | `seller.name` | Uzupełnij nazwę firmy w danych do faktur |
| INV-VAL-011 | `seller.addressLine1` | Uzupełnij adres firmy w danych do faktur |
| INV-VAL-012 | `seller.bankAccount` | Uzupełnij numer rachunku w danych firmy (przelew only) |
| INV-VAL-013 | `buyer.nip` | Dla nabywcy firmy wymagany jest poprawny NIP |
| INV-VAL-014 | `buyer.nip` | Dla osoby prywatnej nie podawaj NIP |
| INV-VAL-015 | `buyer.name` | Uzupełnij nazwę nabywcy |
| INV-VAL-016 | `buyer.addressLine1` | Uzupełnij adres nabywcy |
| INV-VAL-021 | `dueDate` | Termin płatności jest wcześniejszy niż data wystawienia |
| INV-VAL-050 | `lines[i].name` | Każda pozycja musi mieć nazwę |
| INV-VAL-051 | `lines[i].quantity` | Ilość na pozycji musi być większa od zera |
| INV-VAL-052 | `lines[i].unitNetPrice` | Cena netto nie może być ujemna |
| INV-VAL-060 | `lines[i].vatRate` | Dozwolone stawki VAT: 23, 8, 5, 0 KR, zw |
| INV-VAL-061 | `exemptionBasis` | Podaj podstawę zwolnienia z VAT |
| INV-W-NIP | `buyer.nip` | NIP nabywcy ma niepoprawną sumę kontrolną (warning) |
| INV-W-MPP | `splitPayment` | Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15) (warning, gross > 15 000,00) |
