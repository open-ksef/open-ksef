import type { ReactElement } from 'react'

interface KsefIdentifiersCardProps {
  ksefDocumentNumber?: string | null
  ksefReferenceNumber?: string | null
}

export function KsefIdentifiersCard({
  ksefDocumentNumber,
  ksefReferenceNumber,
}: KsefIdentifiersCardProps): ReactElement | null {
  if (!ksefDocumentNumber && !ksefReferenceNumber) {
    return null
  }

  return (
    <section className="ksef-identifiers-card" aria-label="Identyfikatory KSeF">
      <h2 className="ksef-identifiers-card__title">Identyfikatory KSeF</h2>
      <dl className="ksef-identifiers-card__details">
        {ksefDocumentNumber ? (
          <>
            <dt>Numer KSeF</dt>
            <dd>{ksefDocumentNumber}</dd>
          </>
        ) : null}
        {ksefReferenceNumber ? (
          <>
            <dt>Numer referencyjny</dt>
            <dd>{ksefReferenceNumber}</dd>
          </>
        ) : null}
      </dl>
    </section>
  )
}
