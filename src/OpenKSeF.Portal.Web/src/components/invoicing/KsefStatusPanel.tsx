import { useState, type ReactElement } from 'react'
import { Link } from 'react-router-dom'

import type { IssuedInvoiceResponse } from '@/api/types'
import { formatDateTime } from '@/invoicing/format'
import { KsefStatusBadge } from './KsefStatusBadge'

interface KsefStatusPanelProps {
  invoice: IssuedInvoiceResponse
  editHref: string
}

export function KsefStatusPanel({ invoice, editHref }: KsefStatusPanelProps): ReactElement {
  const { status, ksef, number } = invoice
  const [copied, setCopied] = useState(false)

  return (
    <section className="inv-card" aria-labelledby="ksef-panel-title" aria-live="polite">
      <h2 id="ksef-panel-title">KSeF</h2>
      <KsefStatusBadge status={status} />

      {status === 'Draft' ? (
        <>
          <p className="inv-help">Bez numeru. Możesz edytować i usunąć.</p>
          <Link to={editHref}>Edytuj szkic</Link>
        </>
      ) : null}

      {status === 'Sending' ? (
        <>
          {ksef.lastError ? (
            <div className="inv-alert inv-alert--warning" role="status">
              <strong>KSeF nie odpowiada.</strong> Ponowimy wysyłkę automatycznie. Nie wysyłaj faktury drugi raz.
              <div className="inv-help">{ksef.lastError}</div>
            </div>
          ) : null}
          <ol className="inv-steps">
            <li>Sprawdzono ✓</li>
            <li>Nadano numer {number ? <span className="mono">{number}</span> : null} ✓</li>
            <li aria-current="step">Wysłano do KSeF, czekamy na przyjęcie</li>
            <li>Pobranie UPO</li>
          </ol>
          <p className="inv-help">Odświeżamy automatycznie. Możesz zamknąć stronę.</p>
        </>
      ) : null}

      {status === 'Accepted' ? (
        <dl className="inv-readonly">
          <dt>Numer KSeF</dt>
          <dd>
            <span className="mono">{ksef.ksefNumber ?? '—'}</span>{' '}
            {ksef.ksefNumber ? (
              <button
                type="button"
                className="ui-button ui-button--outline ui-button--sm"
                onClick={() => void navigator.clipboard?.writeText(ksef.ksefNumber ?? '').then(() => setCopied(true))}
              >
                {copied ? 'Skopiowano' : 'Kopiuj'}
              </button>
            ) : null}
          </dd>
          <dt>Przyjęta</dt>
          <dd>{formatDateTime(ksef.acceptedAt)}</dd>
          <dt>UPO</dt>
          <dd>{ksef.hasUpo ? 'Pobrane' : 'Oczekuje na UPO'}</dd>
        </dl>
      ) : null}

      {status === 'Rejected' ? (
        <>
          <div className="inv-alert inv-alert--error" role="alert">
            {ksef.statusCode !== null
              ? `Kod KSeF ${ksef.statusCode}: ${ksef.statusMessage ?? 'brak opisu'}`
              : (ksef.statusMessage ?? 'KSeF odrzucił fakturę.')}
            {ksef.lastError && ksef.lastError !== ksef.statusMessage ? <div>{ksef.lastError}</div> : null}
          </div>
          {number ? (
            <p className="inv-help">
              Numer <span className="mono">{number}</span> zostaje — po poprawce wyślemy fakturę z tym samym numerem.
            </p>
          ) : null}
          <Link to={editHref} className="ui-button ui-button--primary ui-button--md">
            Popraw i wyślij ponownie
          </Link>
        </>
      ) : null}
    </section>
  )
}
