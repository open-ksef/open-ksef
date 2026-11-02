import { useState, type ReactElement, type ReactNode } from 'react'
import { Link } from 'react-router-dom'

import type { IssuedInvoiceResponse } from '@/api/types'
import { formatDateTime } from '@/invoicing/format'
import { isReissueBlocked } from '@/invoicing/status'
import { KsefStatusBadge } from './KsefStatusBadge'

interface KsefStatusPanelProps {
  invoice: IssuedInvoiceResponse
  editHref: string
  copyAction: ReactNode
}

export function KsefStatusPanel({ invoice, editHref, copyAction }: KsefStatusPanelProps): ReactElement {
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
              <strong>{ksef.lastError}</strong> Nie wysyłaj faktury drugi raz.
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
            {ksef.statusDetails ? <div>{ksef.statusDetails}</div> : null}
            {ksef.lastError && ksef.lastError !== ksef.statusMessage ? <div>{ksef.lastError}</div> : null}
          </div>
          {isReissueBlocked(invoice) ? (
            <>
              <p className="inv-help">
                Numer <span className="mono">{number}</span> jest już zajęty w KSeF, więc ponowna wysyłka z tym numerem się nie
                uda.
              </p>
              {ksef.duplicateKsefNumber ? (
                <dl className="inv-readonly">
                  <dt>Numer KSeF</dt>
                  <dd className="mono">{ksef.duplicateKsefNumber}</dd>
                </dl>
              ) : null}
              <p>Sprawdź tę fakturę w KSeF. Jeśli to ta sama sprzedaż, nie wystawiaj jej ponownie.</p>
              {copyAction}
              <p className="inv-help">
                Inna sprzedaż? „Kopiuj jako nową” utworzy szkic, który dostanie nowy numer. Jeśli wcześniejsze numery nadano poza
                OpenKSeF, najpierw ustaw wyższy następny numer w Danych do faktur.
              </p>
            </>
          ) : (
            <>
              {number ? (
                <p className="inv-help">
                  Numer <span className="mono">{number}</span> zostaje — po poprawce wyślemy fakturę z tym samym numerem.
                </p>
              ) : null}
              <Link to={editHref} className="ui-button ui-button--primary ui-button--md">
                Popraw i wyślij ponownie
              </Link>
            </>
          )}
        </>
      ) : null}
    </section>
  )
}
