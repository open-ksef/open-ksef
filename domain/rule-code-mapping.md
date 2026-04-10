# Validation Rule Code Mapping

| Code | Class |
|------|-------|
| `INV-VAL-001` | `SupportedDocumentKindRule` |
| `INV-VAL-002` | `FiscalDocumentRequiresLineItemsRule` |
| `INV-VAL-003` | `ProformaCannotEnterFiscalPathRule` |
| `INV-VAL-010` | `SellerLegalNameRequiredRule` |
| `INV-VAL-011` | `SellerNipRequiredForPolishFiscalDocumentRule` |
| `INV-VAL-012` | `BuyerKindMustBeResolvedRule` |
| `INV-VAL-013` | `BusinessBuyerRequiresNipRule` |
| `INV-VAL-020` | `IssueDateRequiredRule` |
| `INV-VAL-021` | `DueDateCannotBeEarlierThanIssueDateRule` |
| `INV-VAL-022` | `SaleDateOrPeriodRequiredRule` |
| `INV-VAL-030` | `DocumentNumberRequiredOnApprovalRule` |
| `INV-VAL-031` | `DocumentNumberMustBeUniqueRule` |
| `INV-VAL-032` | `DocumentNumberPatternRule` |
| `INV-VAL-040` | `CurrencyCodeRequiredRule` |
| `INV-VAL-041` | `ForeignCurrencyBlockedByPolicyRule` |
| `INV-VAL-042` | `ForeignCurrencyRequiresExchangeRateMetadataRule` |
| `INV-VAL-050` | `LineDescriptionRequiredRule` |
| `INV-VAL-051` | `LineQuantityMustBePositiveRule` |
| `INV-VAL-052` | `LineAmountsMustBeInternallyConsistentRule` |
| `INV-VAL-053` | `DocumentTotalsMustMatchLineSumRule` |
| `INV-VAL-060` | `VatTreatmentRequiredRule` |
| `INV-VAL-061` | `ExemptLineRequiresReasonRule` |
| `INV-VAL-062` | `ExemptLineCannotHavePositiveVatRule` |
| `INV-VAL-063` | `VatSummaryMustMatchLinesRule` |
| `INV-VAL-064` | `SplitPaymentRequiresVisibleMarkerRule` |
| `INV-VAL-070` | `AdvanceInvoiceAmountMustBePositiveRule` |
| `INV-VAL-071` | `FinalInvoiceRequiresAdvanceReferencesRule` |
| `INV-VAL-072` | `FinalInvoiceAdvanceSettlementsMustNotOverflowRule` |
| `INV-VAL-073` | `AdvanceReferencesMustMatchCommercialContextRule` |
| `INV-VAL-080` | `CorrectionMustReferenceOriginalDocumentRule` |
| `INV-VAL-081` | `CorrectionReasonRequiredRule` |
| `INV-VAL-082` | `CorrectionMustContainEffectiveChangeRule` |
| `INV-VAL-083` | `ProformaCannotBeCorrectedFiscalyRule` |
| `INV-VAL-090` | `BuyerClassificationMustResolveForKsefRule` |
| `INV-VAL-091` | `DocumentKindMustBeKsefEligibleRule` |
| `INV-VAL-092` | `KsefConfigurationMustBeAvailableRule` |
| `INV-VAL-093` | `AcceptedKsefDocumentCannotBeResentRule` |
| `INV-VAL-100` | `InvalidStateTransitionRule` |
| `INV-VAL-101` | `AcceptedKsefDocumentCannotBeEditedRule` |
| `INV-VAL-102` | `ApprovedDocumentCannotReturnToDraftWhenPolicyForbidsRule` |
| `INV-VAL-110` | `KsefPayloadMappingMustSucceedRule` |
| `INV-VAL-111` | `KsefPayloadSchemaMustBeValidRule` |
| `INV-VAL-112` | `LocalOnlyFieldsOmittedFromKsefPayloadRule` |

## Test coverage

Focused positive and negative rule-path coverage is implemented in:

- `StructureIdentityValidationRuleTests`
- `PartyValidationRuleTests`
- `DateValidationRuleTests`
- `NumberingValidationRuleTests`
- `CurrencyValidationRuleTests`
- `LineTotalsValidationRuleTests`
- `VatValidationRuleTests`
- `AdvanceFinalValidationRuleTests`
- `CorrectionValidationRuleTests`
- `KsefRequirementValidationRuleTests`
- `StateTransitionValidationRuleTests`
- `KsefTechnicalValidationRuleTests`
