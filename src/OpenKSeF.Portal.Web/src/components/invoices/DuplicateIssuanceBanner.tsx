import type { ReactElement } from 'react'

export interface DuplicateIssuance {
  issuedAt: string
  issuedBy: string | null
}

interface DuplicateIssuanceBannerProps {
  issuances: DuplicateIssuance[]
}

export function DuplicateIssuanceBanner({ issuances }: DuplicateIssuanceBannerProps): ReactElement | null {
  if (issuances.length === 0) {
    return null
  }

  return (
    <div className="duplicate-issuance-banner" role="note" aria-label="Wystawione duplikaty">
      <p className="duplicate-issuance-banner__summary">
        Wystawiono {issuances.length} {issuances.length === 1 ? 'duplikat' : 'duplikaty/-ów'}
      </p>
      <ul className="duplicate-issuance-banner__list">
        {issuances.map((issuance, index) => (
          <li key={index} className="duplicate-issuance-banner__item">
            <time dateTime={issuance.issuedAt}>
              {new Date(issuance.issuedAt).toLocaleString('pl-PL')}
            </time>
            {issuance.issuedBy ? (
              <span className="duplicate-issuance-banner__issuer"> — {issuance.issuedBy}</span>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  )
}
