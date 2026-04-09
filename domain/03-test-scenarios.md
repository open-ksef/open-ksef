# 3. Test Scenarios

Format: Given / When / Then

## 3.1 VAT

### VAT-001 Standard VAT invoice in PLN
**Given** draft VAT invoice with seller, B2B buyer with NIP, one line at 23% VAT, consistent totals, currency PLN
**When** user approves the document
**Then** approval succeeds
**And** no blocking validation errors are returned

### VAT-002 Missing VAT treatment on line
**Given** draft VAT invoice with one line without VAT rate and without exemption reason
**When** user approves the document
**Then** approval is blocked with `INV-VAL-060`

### VAT-003 Exempt line without legal reason
**Given** draft VAT invoice with line marked as VAT exempt and no exemption basis
**When** user approves the document
**Then** approval is blocked with `INV-VAL-061`

### VAT-004 Exempt line with non-zero VAT amount
**Given** line marked exempt but carrying positive VAT amount
**When** user approves the document
**Then** approval is blocked with `INV-VAL-062`

### VAT-005 VAT summary mismatch
**Given** document lines sum to VAT 230.00
**And** VAT summary says 229.99
**When** user approves the document
**Then** approval is blocked with `INV-VAL-063`

### VAT-006 Draft warning for split payment marker visibility
**Given** draft invoice flagged split payment
**And** no presentation marker/note configured
**When** draft validation runs
**Then** warning `INV-VAL-064` is returned

## 3.2 Advance invoices and final invoices

### ADV-001 Valid advance invoice
**Given** draft advance invoice with seller, buyer, positive advance amount and consistent totals
**When** user approves
**Then** approval succeeds

### ADV-002 Zero-value advance invoice
**Given** draft advance invoice with total gross 0.00
**When** user approves
**Then** approval is blocked with `INV-VAL-070`

### FIN-001 Final invoice settling two advances
**Given** two approved advance invoices for same seller, buyer and currency
**And** a draft final invoice referencing both
**And** settled advance total is lower than or equal to final invoice total
**When** user approves
**Then** approval succeeds

### FIN-002 Final invoice without advance references
**Given** draft final invoice with no referenced advance invoices
**When** user approves
**Then** approval is blocked with `INV-VAL-071`

### FIN-003 Advance settlement overflow
**Given** final invoice gross total 1000.00
**And** referenced advances sum to 1200.00
**When** user approves
**Then** approval is blocked with `INV-VAL-072`

### FIN-004 Advances from different buyer
**Given** final invoice references advance invoice for different buyer identity
**When** user approves
**Then** approval is blocked with `INV-VAL-073`

## 3.3 Corrections

### COR-001 Valid correction invoice
**Given** approved original VAT invoice
**And** draft correction invoice referencing that original
**And** correction reason is provided
**And** at least one corrected value differs
**When** user approves correction
**Then** approval succeeds

### COR-002 Correction without original reference
**Given** draft correction invoice without original document reference
**When** user approves
**Then** approval is blocked with `INV-VAL-080`

### COR-003 Correction without reason
**Given** draft correction invoice referencing original document
**And** no correction reason
**When** user approves
**Then** approval is blocked with `INV-VAL-081`

### COR-004 Correction with no effective change
**Given** draft correction invoice whose before/after values are identical to original
**When** user approves
**Then** approval is blocked with `INV-VAL-082`

### COR-005 Attempt to correct proforma
**Given** draft correction referencing proforma
**When** user approves
**Then** approval is blocked with `INV-VAL-083`

### COR-006 Correction after KSeF acceptance of original
**Given** original invoice accepted by KSeF
**When** user creates correction document
**Then** creation is allowed
**And** original remains immutable
**And** correction becomes the only legal change path

## 3.4 Proforma

### PRO-001 Valid proforma approval for commercial workflow
**Given** draft proforma with lines and parties
**When** user approves
**Then** approval succeeds if commercial approval is allowed by policy
**And** document remains non-fiscal

### PRO-002 Proforma send to KSeF
**Given** approved proforma
**When** user requests send to KSeF
**Then** operation is blocked with `INV-VAL-003` or `INV-VAL-091`

### PRO-003 Proforma with separate numbering pattern
**Given** policy defines separate numbering format for proforma
**When** document number is assigned
**Then** numbering uses proforma sequence

## 3.5 B2B / B2C / NIP

### BUY-001 B2B invoice with NIP requires KSeF
**Given** buyer classified as Business with valid NIP
**And** document kind is VAT invoice
**When** application computes submission requirement
**Then** result is `Required`

### BUY-002 B2C invoice without NIP does not require KSeF
**Given** buyer classified as Consumer without NIP
**When** application computes submission requirement
**Then** result is `Optional`, `Forbidden`, or `NotApplicable` according to tenant policy
**And** not `Required`

### BUY-003 Unknown buyer kind in draft
**Given** buyer kind is unresolved
**When** draft validation runs
**Then** warning `INV-VAL-012` is returned

### BUY-004 B2B without NIP on approval
**Given** buyer kind is Business
**And** buyer NIP is missing
**When** user approves
**Then** approval is blocked with `INV-VAL-013`

### BUY-005 Unresolved KSeF obligation at send stage
**Given** buyer kind is unknown
**And** policy requires explicit KSeF obligation resolution
**When** user requests send to KSeF
**Then** operation is blocked with `INV-VAL-090`

## 3.6 State transitions

### ST-001 Draft to Approved
**Given** valid draft fiscal document
**When** user approves
**Then** status changes to `Approved`

### ST-002 Approved back to Draft when policy allows
**Given** approved unsent B2C invoice
**And** policy `ApprovedEditableBeforeKsef = true`
**When** user unlocks document for editing
**Then** status returns to `Draft`

### ST-003 Approved back to Draft when policy forbids
**Given** approved unsent invoice
**And** policy `ApprovedEditableBeforeKsef = false`
**When** user attempts to unlock for editing
**Then** operation is blocked with `INV-VAL-102`

### ST-004 Invalid send from Draft
**Given** draft invoice not yet approved
**When** user requests direct send to KSeF and workflow requires approval first
**Then** operation is blocked with `INV-VAL-100`

### ST-005 Submitted to AcceptedByKsef
**Given** approved document successfully submitted to KSeF
**When** KSeF returns success and identifiers
**Then** status becomes `AcceptedByKsef`
**And** KSeF identifiers are stored
**And** aggregate becomes immutable

### ST-006 Submitted to RejectedByKsef
**Given** approved document submitted to KSeF
**When** KSeF returns rejection
**Then** status becomes `RejectedByKsef`
**And** rejection details are stored
**And** document may be edited according to policy

## 3.7 Immutability after KSeF

### IMM-001 Content edit after KSeF success
**Given** document in `AcceptedByKsef`
**When** user edits any fiscal content field
**Then** operation is blocked with `INV-VAL-101`

### IMM-002 Re-send accepted document as original
**Given** document in `AcceptedByKsef`
**When** user requests send again as same original document
**Then** operation is blocked with `INV-VAL-093`

### IMM-003 Duplicate print after KSeF success
**Given** document in `AcceptedByKsef`
**When** user requests duplicate print
**Then** duplicate print is generated
**And** original content is unchanged
**And** duplicate metadata is recorded

## 3.8 Configuration / policy

### CFG-001 Custom numbering per document type
**Given** tenant policy defines numbering for VAT invoice, proforma and correction separately
**When** numbers are assigned
**Then** each kind uses its configured sequence

### CFG-002 Uniqueness scope per year
**Given** tenant policy scopes numbering uniqueness by year
**And** same serial appears in different years
**When** validation runs
**Then** no uniqueness error is returned

### CFG-003 PLN-only tenant blocks EUR
**Given** tenant policy allows only PLN
**And** draft invoice currency is EUR
**When** user approves
**Then** approval is blocked with `INV-VAL-041`

### CFG-004 Foreign currency future-ready warning
**Given** tenant enables EUR in draft mode only
**And** exchange rate metadata missing
**When** draft validation runs
**Then** warning `INV-VAL-042` is returned

### CFG-005 Draft warning severity customization
**Given** tenant config upgrades unresolved buyer classification from warning to error at approval stage
**When** user approves unresolved buyer invoice
**Then** approval is blocked
**And** stable code remains `INV-VAL-090` or configured equivalent

## 3.9 Regression scenarios inspired by profak behaviors

### REG-001 Reusing configurable numbering patterns
**Given** numbering format contains date placeholders and separate group key
**When** next number is assigned
**Then** generated number follows format
**And** counter increments within matching group only

### REG-002 Correction keeps relation to original and root original
**Given** invoice A
**And** correction B referencing A
**And** correction C referencing B in UI flow
**When** system normalizes correction chain
**Then** root original remains traceable
**And** latest correction relation remains explicit

### REG-003 English print does not change fiscal payload
**Given** approved VAT invoice
**When** user requests English print view
**Then** rendered labels are English
**And** KSeF payload mapping is unchanged
**And** document kind remains VAT invoice

### REG-004 Duplicate is presentation only
**Given** approved invoice with document number `FV/1/2026`
**When** user issues duplicate print
**Then** no new fiscal aggregate is created
**And** no new numbering sequence is consumed

## 3.10 Negative technical KSeF scenarios

### KTF-001 Mapping failure due to unsupported VAT combination
**Given** domain invoice contains VAT treatment unsupported by current mapper
**When** user requests send to KSeF
**Then** operation is blocked with `INV-VAL-110`

### KTF-002 Payload schema validation failure
**Given** mapper generated payload
**And** open-ksef technical validator rejects schema
**When** send validation runs
**Then** operation is blocked with `INV-VAL-111`

### KTF-003 Missing KSeF credentials
**Given** submission is required
**And** tenant has no valid KSeF credentials
**When** user requests send
**Then** operation is blocked with `INV-VAL-092`
