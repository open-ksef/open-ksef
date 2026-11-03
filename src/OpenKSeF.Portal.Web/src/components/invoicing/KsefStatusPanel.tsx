import type { ReactElement, ReactNode } from 'react'
import { useState } from 'react'
import { Link } from 'react-router-dom'

import type { IssuedInvoiceResponse } from '@/api/types'
import { Icon } from '@/components/Icon'
import { formatDateTime } from '@/invoicing/format'
import { rejectionCause } from '@/invoicing/status'
import type { TokenReturnState } from '@/pages/CompanyToken'

interface KsefStatusPanelProps {
  invoice: IssuedInvoiceResponse
  tenantId: string
  editHref: string
  copyAction: ReactNode
  deleteAction: ReactNode
  downloads: ReactNode
}

export function KsefStatusPanel({ invoice, tenantId, editHref, copyAction, deleteAction, downloads }: KsefStatusPanelProps): ReactElement {
  const { status, ksef, number } = invoice
  const [copied, setCopied] = useState(false)

  if (status === 'Draft') {
    return (
      <section className="status-panel" aria-labelledby="ksef-panel-title">
        <div>
          <h2 id="ksef-panel-title">Szkic</h2>
          <p>Bez numeru. Możesz edytować i usunąć.</p>
        </div>
        <div className="status-panel__next">
          <Link to={editHref} className="ui-button ui-button--primary ui-button--md">
            <Icon name="pencil" /> Edytuj szkic
          </Link>
          {deleteAction}
        </div>
      </section>
    )
  }

  if (status === 'Sending') {
    return (
      <section className="status-panel" aria-labelledby="ksef-panel-title" aria-live="polite">
        <div>
          <h2 id="ksef-panel-title">Wysyłamy fakturę do KSeF</h2>
          <ol className="status-steps">
            <li>
              <Icon name="check" size={14} /> Sprawdzono
            </li>
            <li>
              <Icon name="check" size={14} /> Nadano numer {number ? <span className="mono">{number}</span> : null}
            </li>
            <li aria-current="step">
              <Icon name="loader" size={14} /> Wysyłamy do KSeF, czekamy na przyjęcie
            </li>
            <li className="status-steps__todo">
              <Icon name="circle-dashed" size={14} /> Pobranie UPO
            </li>
          </ol>
        </div>
        <div className="status-panel__next">
          {ksef.lastError ? (
            <div className="inv-alert inv-alert--warning" role="status">
              <strong>{ksef.lastError}</strong> Nie wysyłaj faktury drugi raz.
            </div>
          ) : null}
          <p className="inv-help">Odświeżamy automatycznie. Możesz zamknąć stronę.</p>
        </div>
      </section>
    )
  }

  if (status === 'Accepted') {
    return (
      <section className="status-panel" aria-labelledby="ksef-panel-title">
        <div>
          <h2 id="ksef-panel-title">KSeF przyjął tę fakturę</h2>
          {!ksef.hasUpo && ksef.lastError ? (
            <div className="inv-alert inv-alert--warning" role="status">
              <strong>{ksef.lastError}</strong> Faktura jest przyjęta w KSeF i ma numer. Brak UPO nie wymaga ponownego wystawienia.
            </div>
          ) : null}
          <dl className="kv">
            <dt>Numer KSeF</dt>
            <dd>
              <span className="mono">{ksef.ksefNumber ?? '—'}</span>{' '}
              {ksef.ksefNumber ? (
                <button
                  type="button"
                  className="ui-button ui-button--outline ui-button--sm"
                  aria-label="Kopiuj numer KSeF"
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
          <p className="inv-help">Faktury przyjętej w KSeF nie można usunąć ani edytować.</p>
        </div>
        <div className="status-panel__next">
          {downloads}
          {copyAction}
        </div>
      </section>
    )
  }

  const cause = rejectionCause(invoice)
  const details = [ksef.statusDetails, ksef.lastError && ksef.lastError !== ksef.statusMessage ? ksef.lastError : null].filter(Boolean)
  const tokenState: TokenReturnState = { rejectedInvoice: { id: invoice.id, number, tenantId } }

  return (
    <section className="status-panel status-panel--rejected" aria-labelledby="ksef-panel-title">
      <div>
        <span className="inv-caption">Co się stało</span>
        <h2 id="ksef-panel-title">KSeF nie przyjął tej faktury</h2>
        <p role="alert">{ksef.statusMessage ?? 'KSeF odrzucił fakturę.'}</p>
        {cause === 'duplicate' ? (
          <>
            <p className="status-panel__line">
              Numer <span className="mono">{number}</span> jest już zajęty w KSeF, więc ponowna wysyłka z tym numerem się nie uda.
            </p>
            {ksef.duplicateKsefNumber ? (
              <dl className="kv">
                <dt>Numer KSeF</dt>
                <dd className="mono">{ksef.duplicateKsefNumber}</dd>
              </dl>
            ) : null}
            <p className="status-panel__line">Sprawdź tę fakturę w KSeF. Jeśli to ta sama sprzedaż, nie wystawiaj jej ponownie.</p>
          </>
        ) : null}
        {ksef.statusCode !== null ? (
          <p className="mono status-panel__code" data-testid="ksef-code">
            Kod KSeF {ksef.statusCode}
          </p>
        ) : null}
      </div>
      <div className="status-panel__next">
        <span className="inv-caption">Co teraz</span>
        {cause === 'token' ? (
          <>
            <Link to="/company/token" state={tokenState} className="ui-button ui-button--primary ui-button--md" data-testid="replace-token-link">
              <Icon name="key-round" /> Podmień token KSeF
            </Link>
            <Link to={editHref} className="status-panel__link" data-testid="token-resend-link">
              Token już podmieniony? Wyślij ponownie
            </Link>
            {number ? (
              <p className="inv-help">
                Numer <span className="mono">{number}</span> zostaje — po podmianie tokenu wyślemy fakturę z tym samym numerem.
              </p>
            ) : null}
          </>
        ) : null}
        {cause === 'data' ? (
          <>
            <Link to={editHref} className="ui-button ui-button--primary ui-button--md">
              <Icon name="pencil" /> Popraw i wyślij ponownie
            </Link>
            {number ? (
              <p className="inv-help">
                Numer <span className="mono">{number}</span> zostaje — po poprawce wyślemy fakturę z tym samym numerem.
              </p>
            ) : null}
          </>
        ) : null}
        {cause === 'duplicate' ? (
          <>
            {copyAction}
            <p className="inv-help">
              Inna sprzedaż? „Kopiuj jako nową” utworzy szkic, który dostanie nowy numer. Jeśli wcześniejsze numery nadano poza OpenKSeF, najpierw
              ustaw wyższy następny numer w <Link to="/company/invoice-data">Danych do faktur</Link>.
            </p>
          </>
        ) : null}
      </div>
      {details.length > 0 ? (
        <details className="status-panel__details" data-testid="ksef-details">
          <summary>Szczegóły techniczne</summary>
          {details.map((line) => (
            <p key={line} className="mono">
              {line}
            </p>
          ))}
        </details>
      ) : null}
    </section>
  )
}
