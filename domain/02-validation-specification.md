# 2. Validation Specification — Technical + Domain

## 2.1 Validation goals
Validation must:
- distinguish draft guidance from blocking compliance checks,
- remain composable and configurable,
- expose stable machine-readable rule codes,
- separate domain correctness from KSeF technical compatibility.

## 2.2 Validation stages
- `Draft` — soft validation and early errors that do not block editing.
- `Approve` — hard blocking domain validation before business approval.
- `SendToKsef` — hard blocking domain + technical integration validation before submission.

## 2.3 Severity
- `Warning` — non-blocking, user guidance.
- `Error` — blocking at stage where rule is enforced.

## 2.4 Validation categories
- `Structure`
- `Parties`
- `Dates`
- `Numbering`
- `Currency`
- `Lines`
- `Vat`
- `Totals`
- `BuyerClassification`
- `AdvanceSettlement`
- `Correction`
- `KsefRequirement`
- `KsefPayload`
- `StateTransition`
- `Immutability`
- `Configuration`

## 2.5 Proposed interfaces
```csharp
public enum ValidationStage
{
    Draft,
    Approve,
    SendToKsef
}

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ValidationMessage(
    string Code,
    ValidationSeverity Severity,
    ValidationStage Stage,
    string UserMessage,
    string TechnicalMessage,
    string? Path = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record ValidationContext(
    ValidationStage Stage,
    TenantId TenantId,
    Instant Now,
    IPolicySnapshot Policies,
    bool IsKsefSubmissionRequested,
    bool IsNumberAssigned,
    IReadOnlyDictionary<string, object?> Items);

public sealed record ValidationResult(IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
}

public interface IValidationRule<in T>
{
    string Code { get; }
    bool AppliesTo(ValidationContext context, T target);
    IEnumerable<ValidationMessage> Validate(ValidationContext context, T target);
}
```

Refactor note:
- these rules target the new issuing aggregate `Invoice`,
- the current synchronized/read-side models should not become the place where new domain validation accumulates,
- if the legacy PostgreSQL schema is renamed from `InvoiceHeader` / `InvoiceLine` toward `SyncedInvoice` / `SyncedInvoiceLine`, validation boundaries should stay unchanged because they belong to the new domain model, not to the sync tables.

## 2.6 Pipeline split
- `IDomainValidationRule<Invoice>`
- `IDomainValidationRule<InvoiceLine>`
- `IKsefTechnicalValidationRule<KsefInvoicePayload>`
- `IStateTransitionRule<Invoice>`

Recommended orchestrators:
- `DraftValidationService`
- `ApprovalValidationService`
- `KsefSubmissionValidationService`

## 2.7 Configuration mechanism
Use a policy provider, not conditionals scattered in handlers.

Recommended abstractions:
```csharp
public interface IPolicyProvider
{
    Task<IPolicySnapshot> GetForTenantAsync(TenantId tenantId, CancellationToken ct);
}

public interface IPolicySnapshot
{
    NumberingPolicy Numbering { get; }
    KsefPolicy Ksef { get; }
    VatPolicy Vat { get; }
    EditPolicy Edit { get; }
    ValidationPolicy Validation { get; }
    CurrencyPolicy Currency { get; }
}
```

Feature flags belong at application/infrastructure boundary, but their output should be materialized as strongly typed policies.

## 2.8 Rule catalog

## 2.8.1 Structure / identity

### INV-VAL-001
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: `DocumentKind` must be set to a supported v1 kind.
- User: `Wybierz poprawny typ dokumentu.`
- Technical: `Unsupported or missing DocumentKind.`

### INV-VAL-002
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: fiscal document must contain at least one line.
- User: `Faktura musi zawierać co najmniej jedną pozycję.`
- Technical: `No line items found for fiscal document.`

### INV-VAL-003
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: proforma cannot be marked as fiscal/KSeF-submittable.
- User: `Proforma nie jest dokumentem fiskalnym i nie może zostać wysłana do KSeF.`
- Technical: `Proforma entered fiscal/KSeF path.`

## 2.8.2 Parties

### INV-VAL-010
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: seller legal name is required.
- User: `Uzupełnij nazwę sprzedawcy.`
- Technical: `SellerSnapshot.Name is missing.`

### INV-VAL-011
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: seller NIP is required for fiscal Polish-default documents.
- User: `Uzupełnij NIP sprzedawcy.`
- Technical: `SellerSnapshot.Nip is missing.`

### INV-VAL-012
- Severity: Warning
- Stage: Draft
- Rule: buyer kind unresolved.
- User: `Nie określono typu nabywcy (B2B/B2C). Może to wpływać na obowiązek wysyłki do KSeF.`
- Technical: `Buyer classification unresolved.`

### INV-VAL-013
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: B2B buyer requires valid NIP.
- User: `Dla nabywcy B2B wymagany jest poprawny NIP.`
- Technical: `BuyerKind=Business but BuyerSnapshot.Nip missing/invalid.`

## 2.8.3 Dates

### INV-VAL-020
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: issue date is required.
- User: `Uzupełnij datę wystawienia.`
- Technical: `IssueDate missing.`

### INV-VAL-021
- Severity: Warning
- Stage: Draft
- Rule: due date earlier than issue date.
- User: `Termin płatności jest wcześniejszy niż data wystawienia.`
- Technical: `DueDate < IssueDate.`

### INV-VAL-022
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: sale date/period required where policy marks it mandatory.
- User: `Uzupełnij datę lub okres sprzedaży.`
- Technical: `Sale date/period missing.`

## 2.8.4 Numbering / uniqueness

### INV-VAL-030
- Severity: Error
- Stage: Approve
- Rule: number must be assigned if numbering-on-approval is enabled.
- User: `Dokument nie ma nadanego numeru.`
- Technical: `DocumentNumber missing at approval.`

### INV-VAL-031
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: document number must be unique within configured scope.
- User: `Numer dokumentu jest już użyty.`
- Technical: `DocumentNumber uniqueness violation in policy scope.`

### INV-VAL-032
- Severity: Warning
- Stage: Draft
- Rule: number format deviates from configured pattern.
- User: `Numer dokumentu nie pasuje do domyślnego wzorca.`
- Technical: `DocumentNumber does not match NumberingPolicy pattern.`

## 2.8.5 Currency

### INV-VAL-040
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: currency code required.
- User: `Uzupełnij walutę dokumentu.`
- Technical: `CurrencyCode missing.`

### INV-VAL-041
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: non-PLN not allowed when tenant policy is PLN-only.
- User: `Ta konfiguracja nie obsługuje jeszcze faktur w tej walucie.`
- Technical: `Currency blocked by CurrencyPolicy.`

### INV-VAL-042
- Severity: Warning
- Stage: Draft
- Rule: non-PLN without exchange rate metadata.
- User: `Dla waluty obcej może być wymagany kurs przeliczeniowy.`
- Technical: `Foreign currency document missing exchange rate metadata.`

## 2.8.6 Lines / totals

### INV-VAL-050
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: line description required.
- User: `Każda pozycja musi mieć opis.`
- Technical: `Line description missing.`

### INV-VAL-051
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: quantity must be greater than zero for standard lines.
- User: `Ilość na pozycji musi być większa od zera.`
- Technical: `Line quantity invalid for non-correction line.`

### INV-VAL-052
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: line totals must be internally consistent.
- User: `Kwoty na pozycji są niespójne.`
- Technical: `Line net/vat/gross mismatch.`

### INV-VAL-053
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: document totals must equal sum of lines.
- User: `Podsumowanie dokumentu nie zgadza się z sumą pozycji.`
- Technical: `Document totals mismatch.`

## 2.8.7 VAT

### INV-VAL-060
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: each line must define VAT treatment.
- User: `Każda pozycja musi mieć określoną stawkę VAT lub podstawę zwolnienia.`
- Technical: `Line missing VatRate/VatExemptionReason.`

### INV-VAL-061
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: exemption basis required when VAT is exempt/non-taxable.
- User: `Dla pozycji zwolnionej z VAT podaj podstawę zwolnienia.`
- Technical: `Exempt line missing TaxExemptionReason.`

### INV-VAL-062
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: exempt line cannot carry positive VAT.
- User: `Pozycja zwolniona z VAT nie może zawierać kwoty VAT.`
- Technical: `Exempt line has non-zero VAT amount.`

### INV-VAL-063
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: VAT summary must reconcile to lines.
- User: `Podsumowanie VAT jest niespójne z pozycjami.`
- Technical: `Vat breakdown mismatch.`

### INV-VAL-064
- Severity: Warning
- Stage: Draft
- Rule: document flagged split payment but no visible marker/note prepared.
- User: `Dokument wskazuje split payment, ale brakuje oznaczenia do prezentacji.`
- Technical: `Split payment flag set without presentation marker.`

## 2.8.8 Advance / final invoice

### INV-VAL-070
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: advance invoice amount must be greater than zero.
- User: `Faktura zaliczkowa musi zawierać dodatnią kwotę zaliczki.`
- Technical: `Advance amount <= 0.`

### INV-VAL-071
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: final invoice must reference at least one advance invoice.
- User: `Faktura końcowa musi wskazywać co najmniej jedną fakturę zaliczkową.`
- Technical: `Final invoice missing advance references.`

### INV-VAL-072
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: final invoice settled advances cannot exceed allowed amount.
- User: `Suma rozliczanych zaliczek przekracza wartość faktury końcowej.`
- Technical: `Advance settlement overflow.`

### INV-VAL-073
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: advance references must belong to same seller/buyer/currency context.
- User: `Rozliczane zaliczki muszą dotyczyć tego samego kontraktu sprzedażowego.`
- Technical: `Advance references inconsistent with final invoice.`

## 2.8.9 Correction

### INV-VAL-080
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: correction must reference original fiscal document.
- User: `Faktura korygująca musi wskazywać dokument korygowany.`
- Technical: `CorrectionReference missing.`

### INV-VAL-081
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: correction reason required.
- User: `Podaj przyczynę korekty.`
- Technical: `Correction reason missing.`

### INV-VAL-082
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: correction must change something material or formal.
- User: `Korekta nie zawiera żadnej zmiany względem dokumentu pierwotnego.`
- Technical: `No detectable difference from original document.`

### INV-VAL-083
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: proforma cannot be corrected with fiscal correction invoice.
- User: `Proforma nie podlega korekcie fiskalnej.`
- Technical: `Correction references non-fiscal proforma.`

## 2.8.10 KSeF requirement / submission

### INV-VAL-090
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: buyer classification must resolve when KSeF obligation depends on it.
- User: `Nie można ustalić, czy dokument wymaga wysyłki do KSeF.`
- Technical: `KSeF obligation unresolved due to buyer classification ambiguity.`

### INV-VAL-091
- Severity: Error
- Stage: SendToKsef
- Rule: document kind must be KSeF-eligible.
- User: `Ten typ dokumentu nie może zostać wysłany do KSeF.`
- Technical: `Document kind blocked for KSeF submission.`

### INV-VAL-092
- Severity: Error
- Stage: SendToKsef
- Rule: required KSeF credentials/configuration missing.
- User: `Brakuje konfiguracji wymaganej do wysyłki do KSeF.`
- Technical: `KSeF credentials/config not available.`

### INV-VAL-093
- Severity: Error
- Stage: SendToKsef
- Rule: accepted-by-KSeF document cannot be re-sent as editable original.
- User: `Dokument wysłany i przyjęty przez KSeF jest niezmienny.`
- Technical: `Attempted send/edit on immutable document.`

## 2.8.11 State transition / immutability

### INV-VAL-100
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: invalid transition from current state.
- User: `Nie można wykonać tej operacji w aktualnym stanie dokumentu.`
- Technical: `Invalid state transition.`

### INV-VAL-101
- Severity: Error
- Stage: Approve, SendToKsef
- Rule: accepted-by-KSeF document cannot be edited.
- User: `Dokument po skutecznej wysyłce do KSeF nie może być edytowany.`
- Technical: `Immutable aggregate mutation attempted.`

### INV-VAL-102
- Severity: Error
- Stage: Approve
- Rule: approved document cannot return to draft when policy forbids it.
- User: `Ta konfiguracja nie pozwala edytować zatwierdzonego dokumentu.`
- Technical: `Approved->Draft forbidden by policy.`

## 2.8.12 Technical KSeF payload

### INV-VAL-110
- Severity: Error
- Stage: SendToKsef
- Rule: KSeF payload mapping failed.
- User: `Nie udało się przygotować danych dokumentu do wysyłki do KSeF.`
- Technical: `Domain-to-KSeF payload mapping failed.`

### INV-VAL-111
- Severity: Error
- Stage: SendToKsef
- Rule: generated KSeF payload fails schema-level validation.
- User: `Dokument nie przeszedł walidacji technicznej wymaganej przez KSeF.`
- Technical: `KSeF schema validation failed.`

### INV-VAL-112
- Severity: Warning
- Stage: Draft
- Rule: some fields are present locally but not mapped to KSeF payload.
- User: `Część danych ma charakter lokalny i nie zostanie wysłana do KSeF.`
- Technical: `Local-only fields omitted from KSeF payload.`

## 2.9 Rule execution matrix
- `Draft`: warnings + selected errors surfaced non-blockingly in UI
- `Approve`: all domain hard rules
- `SendToKsef`: all approve rules + KSeF requirement rules + technical payload rules

## 2.10 Compliance vs configurable policy

### Hard law / compliance-oriented rules
Typically non-configurable as blocking:
- required seller identity
- invoice/correction reference integrity
- total reconciliation
- VAT treatment completeness
- no proforma submission to KSeF
- immutability after successful KSeF acceptance

### Configurable policy candidates
- numbering format and scope
- approved document editability before KSeF
- whether unresolved buyer kind is warning at draft only or also blocks approval
- whether foreign currency is enabled
- some presentation-related warnings
- optional markers such as TP/GTU enforcement level, where legal interpretation allows tenant-specific strictness

## 2.11 Recommendation on implementation
Do not embed these rules in EF entities or controllers.
Use:
- one rule per file/class,
- deterministic codes,
- unit tests per code,
- grouped registration by stage.

Recommended folder layout:
- `Domain/Validation/Rules/Draft/*`
- `Domain/Validation/Rules/Approve/*`
- `Domain/Validation/Rules/SendToKsef/*`
- `Infrastructure/KSeF/Validation/*`
