import { useQuery } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'

import { getIssuedInvoice, getIssuedInvoiceUpo, getIssuedInvoiceXml } from '@/api/endpoints/issuedInvoices'
import type { IssuedInvoiceResponse } from '@/api/types'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Skeleton } from '@/components/Skeleton'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { KsefStatusPanel } from '@/components/invoicing/KsefStatusPanel'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import { formatDate, formatMoney, formatNrb } from '@/invoicing/format'
import { downloadFile, fileStem, ksefPollInterval } from '@/invoicing/status'
import { vatRateLabel } from '@/invoicing/totals'

const paymentLabels = { Transfer: 'Przelew', Cash: 'Gotówka', Card: 'Karta' } as const

export function IssuedInvoiceDetailsPage(): ReactElement {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  const tenantsQuery = useTenants()
  const tenantId = searchParams.get('tenantId') || tenantsQuery.data?.[0]?.id || ''
  const [openedAt] = useState(() => Date.now())

  const invoiceQuery = useQuery({
    queryKey: ['issued-invoice', tenantId, id],
    queryFn: () => getIssuedInvoice(tenantId, id),
    enabled: Boolean(tenantId && id),
    refetchInterval: (query) => ksefPollInterval(query.state.data?.status, Date.now() - openedAt),
  })
  const error = tenantsQuery.error ?? invoiceQuery.error

  return (
    <div className="invoicing">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować faktury'}
          onRetry={() => void invoiceQuery.refetch()}
        />
      ) : !invoiceQuery.data ? (
        <Skeleton lines={8} />
      ) : (
        <InvoiceDetails tenantId={tenantId} invoice={invoiceQuery.data} />
      )}
    </div>
  )
}

function InvoiceDetails({ tenantId, invoice }: { tenantId: string; invoice: IssuedInvoiceResponse }): ReactElement {
  const [failure, setFailure] = useState<string | null>(null)
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const stem = fileStem(invoice.number, invoice.id)
  const accepted = invoice.status === 'Accepted'

  async function download(load: () => Promise<string>, fileName: string): Promise<void> {
    setFailure(null)
    try {
      downloadFile(await load(), fileName, 'application/xml')
    } catch (downloadError) {
      setFailure(downloadError instanceof Error ? downloadError.message : 'Nie udało się pobrać pliku')
    }
  }

  return (
    <>
      <header className="inv-header">
        <Link to={`/issued?${tenantQuery}`} className="back-link">
          ← Wystawione
        </Link>
        <h1 className="mono">{invoice.number ?? 'Szkic'}</h1>
        <KsefStatusBadge status={invoice.status} />
        <EnvBadge environment={invoice.ksef.environment} />
        <div className="inv-header__actions">
          {accepted ? (
            <>
              <button
                type="button"
                className="ui-button ui-button--outline ui-button--md"
                onClick={() => void download(() => getIssuedInvoiceXml(tenantId, invoice.id), `${stem}.xml`)}
              >
                Pobierz XML
              </button>
              {invoice.ksef.hasUpo ? (
                <button
                  type="button"
                  className="ui-button ui-button--outline ui-button--md"
                  onClick={() => void download(() => getIssuedInvoiceUpo(tenantId, invoice.id), `${stem}_UPO.xml`)}
                >
                  Pobierz UPO
                </button>
              ) : null}
            </>
          ) : null}
        </div>
      </header>

      {failure ? <ErrorBanner message={failure} /> : null}

      <div className="inv-columns">
        <div className="inv-columns__main">
          <section className="inv-card" aria-labelledby="parties-title">
            <h2 id="parties-title">Strony i daty</h2>
            <div className="inv-grid">
              <Party title="Sprzedawca" {...invoice.seller} />
              <Party title="Nabywca" {...invoice.buyer} />
              <dl className="inv-readonly">
                <dt>Data wystawienia</dt>
                <dd>{formatDate(invoice.issueDate)}</dd>
                <dt>Data sprzedaży</dt>
                <dd>{formatDate(invoice.saleDate)}</dd>
                <dt>Termin płatności</dt>
                <dd>{formatDate(invoice.dueDate)}</dd>
                <dt>Płatność</dt>
                <dd>
                  {paymentLabels[invoice.paymentMethod]}
                  {invoice.splitPayment ? ', mechanizm podzielonej płatności' : ''}
                </dd>
                {invoice.paymentMethod === 'Transfer' ? (
                  <>
                    <dt>Rachunek</dt>
                    <dd className="mono">{formatNrb(invoice.seller.bankAccount)}</dd>
                  </>
                ) : null}
              </dl>
            </div>
          </section>

          <section className="inv-card" aria-labelledby="lines-title">
            <h2 id="lines-title">Pozycje</h2>
            <div className="inv-table-wrap">
              <table className="inv-table">
                <thead>
                  <tr>
                    <th scope="col">Lp</th>
                    <th scope="col">Nazwa</th>
                    <th scope="col">J.m.</th>
                    <th scope="col" className="num">Ilość</th>
                    <th scope="col" className="num">Cena netto</th>
                    <th scope="col">VAT</th>
                    <th scope="col" className="num">Netto</th>
                  </tr>
                </thead>
                <tbody>
                  {invoice.lines.map((line) => (
                    <tr key={line.lineNo}>
                      <td>{line.lineNo}</td>
                      <td>{line.name}</td>
                      <td>{line.unit}</td>
                      <td className="num">{line.quantity.toLocaleString('pl-PL', { maximumFractionDigits: 6 })}</td>
                      <td className="num">{formatMoney(line.unitNetPrice, invoice.currency)}</td>
                      <td>{vatRateLabel(line.vatRate)}</td>
                      <td className="num">{formatMoney(line.netAmount, invoice.currency)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {invoice.exemptionBasis ? <p className="inv-help">Podstawa zwolnienia: {invoice.exemptionBasis.text}</p> : null}
          </section>
        </div>

        <aside className="inv-columns__side" aria-label="Status i podsumowanie">
          <KsefStatusPanel invoice={invoice} editHref={`/issued/${encodeURIComponent(invoice.id)}/edit?${tenantQuery}`} />
          <VatSummary
            rows={invoice.vatSummary}
            totalNet={invoice.totalNet}
            totalVat={invoice.totalVat}
            totalGross={invoice.totalGross}
            currency={invoice.currency}
          />
        </aside>
      </div>
    </>
  )
}

interface PartyProps {
  title: string
  nip: string | null
  name: string | null
  addressLine1: string | null
  addressLine2: string | null
}

function Party({ title, nip, name, addressLine1, addressLine2 }: PartyProps): ReactElement {
  return (
    <div>
      <h3>{title}</h3>
      <p>
        {[name ?? '—', nip ? `NIP ${nip}` : null, addressLine1, addressLine2]
          .filter(Boolean)
          .map((line) => (
            <span key={line}>
              {line}
              <br />
            </span>
          ))}
      </p>
    </div>
  )
}
