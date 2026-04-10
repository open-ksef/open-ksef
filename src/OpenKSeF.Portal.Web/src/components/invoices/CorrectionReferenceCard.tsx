import type { ReactElement } from 'react'

export interface CorrectionReference {
  originalInvoiceId: string
  originalDocumentNumber: string
  reasonKind: string
  reasonDescription: string | null
}

interface CorrectionReferenceCardProps {
  reference: CorrectionReference
}

export function CorrectionReferenceCard({ reference }: CorrectionReferenceCardProps): ReactElement {
  return (
    <section className="correction-reference-card" aria-label="Odniesienie do oryginału">
      <h2 className="correction-reference-card__title">Korekta do faktury</h2>
      <dl className="correction-reference-card__details">
        <dt>Numer oryginału</dt>
        <dd>{reference.originalDocumentNumber}</dd>
        <dt>Powód korekty</dt>
        <dd>{reference.reasonKind}</dd>
        {reference.reasonDescription ? (
          <>
            <dt>Opis powodu</dt>
            <dd>{reference.reasonDescription}</dd>
          </>
        ) : null}
      </dl>
    </section>
  )
}
