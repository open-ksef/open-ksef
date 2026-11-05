import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { useState, type ReactElement } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'

import {
  copyIssuedInvoice,
  deleteIssuedInvoice,
  getIssuedInvoice,
  getIssuedInvoiceUpo,
  getIssuedInvoiceXml,
} from '@/api/endpoints/issuedInvoices'
import type { IssuedInvoiceResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { KsefStatusPanel } from '@/components/invoicing/KsefStatusPanel'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import { renderBilingualPdf, type PrintVariant } from '@/invoicing/bilingualPrint'
import { countryName } from '@/invoicing/foreign'
import { formatDate, formatDecimal, formatIban, formatMoney, formatNrb } from '@/invoicing/format'
import { downloadFile, fileStem, isReissueBlocked, ksefPollInterval, renderInvoicePdf } from '@/invoicing/status'
import { vatRateLabel } from '@/invoicing/totals'

const paymentLabels = { Transfer: 'Przelew', Cash: 'Gotówka', Card: 'Karta' } as const

export function IssuedInvoiceDetailsPage(): ReactElement {
  const { id = '' } = useParams()
  const tenantsQuery = useTenants()
  const { tenantId } = useCompany()
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
  const [deleteOpen, setDeleteOpen] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const tenantQuery = `tenantId=${encodeURIComponent(tenantId)}`
  const listSearch = (location.state as { listSearch?: string } | null)?.listSearch
  const stem = fileStem(invoice.number, invoice.id)
  const { ksefNumber, verificationUrl } = invoice.ksef
  const accepted = invoice.status === 'Accepted'
  const isForeign = invoice.buyer.kind === 'Foreign'

  function showFailure(error: unknown, fallback: string): void {
    setFailure(error instanceof Error ? error.message : fallback)
  }

  async function download(load: () => Promise<string>, fileName: string): Promise<void> {
    setFailure(null)
    try {
      downloadFile(await load(), fileName, 'application/xml')
    } catch (downloadError) {
      showFailure(downloadError, 'Nie udało się pobrać pliku')
    }
  }

  const copy = useMutation({
    mutationFn: () => copyIssuedInvoice(tenantId, invoice.id),
    onMutate: () => setFailure(null),
    onSuccess: (draft) => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/edit?${tenantQuery}`)
    },
    onError: (error) => showFailure(error, 'Nie udało się skopiować faktury'),
  })

  const pdf = useMutation({
    mutationFn: async ({ nrKSeF, qrCode, variant }: { nrKSeF: string; qrCode: string; variant: PrintVariant | null }) => {
      const xml = await getIssuedInvoiceXml(tenantId, invoice.id)
      const blob = variant ? await renderBilingualPdf(xml, nrKSeF, qrCode, variant) : await renderInvoicePdf(xml, nrKSeF, qrCode)
      downloadFile(blob, variant ? `${stem}_${variant}.pdf` : `${stem}.pdf`, 'application/pdf')
    },
    onMutate: () => setFailure(null),
    onError: (error) => showFailure(error, 'Nie udało się wygenerować PDF'),
  })

  const remove = useMutation({
    mutationFn: () => deleteIssuedInvoice(tenantId, invoice.id, invoice.version),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued?${tenantQuery}`, { replace: true })
      queryClient.removeQueries({ queryKey: ['issued-invoice', tenantId, invoice.id] })
    },
    onError: (error) => {
      setDeleteOpen(false)
      showFailure(error, 'Nie udało się usunąć szkicu')
    },
  })

  const copyButton = (
    <button
      type="button"
      className={`ui-button ui-button--${isReissueBlocked(invoice) ? 'primary' : 'outline'} ui-button--md`}
      disabled={copy.isPending}
      onClick={() => copy.mutate()}
    >
      <Icon name="copy" /> {copy.isPending ? 'Kopiowanie…' : 'Kopiuj jako nową'}
    </button>
  )

  const pdfButton = (label: string, variant: PrintVariant | null, primary: boolean) =>
    ksefNumber && verificationUrl ? (
      <button
        type="button"
        className={`ui-button ui-button--${primary ? 'primary' : 'outline'} ui-button--md`}
        disabled={pdf.isPending}
        onClick={() => pdf.mutate({ nrKSeF: ksefNumber, qrCode: verificationUrl, variant })}
      >
        <Icon name="file-down" /> {pdf.isPending && pdf.variables.variant === variant ? 'Generowanie…' : label}
      </button>
    ) : null

  const downloads = (
    <>
      {isForeign ? (
        <>
          {pdfButton('Pobierz PDF (EN/PL)', 'EN-PL', true)}
          {pdfButton('Pobierz PDF (PL/EN)', 'PL-EN', false)}
          {pdfButton('Pobierz PDF (wzór MF)', null, false)}
        </>
      ) : (
        pdfButton('Pobierz PDF', null, true)
      )}
      <div className="status-panel__row">
        <button
          type="button"
          className="ui-button ui-button--outline ui-button--md"
          onClick={() => void download(() => getIssuedInvoiceXml(tenantId, invoice.id), `${stem}.xml`)}
        >
          Pobierz XML
        </button>
        <button
          type="button"
          className="ui-button ui-button--outline ui-button--md"
          disabled={!invoice.ksef.hasUpo}
          onClick={() => void download(() => getIssuedInvoiceUpo(tenantId, invoice.id), `${stem}_UPO.xml`)}
        >
          Pobierz UPO
        </button>
      </div>
    </>
  )

  return (
    <section className="invoice-details">
      <Link to={`/issued${listSearch ?? `?${tenantQuery}`}`} className="back-link">
        <Icon name="arrow-left" size={14} /> Wystawione
      </Link>
      <header className="page-header invoice-details-head">
        <h1>{invoice.number ?? 'Szkic'}</h1>
        <KsefStatusBadge status={invoice.status} />
        <EnvBadge environment={invoice.ksef.environment} />
      </header>

      {failure ? <ErrorBanner message={failure} /> : null}

      <KsefStatusPanel
        invoice={invoice}
        tenantId={tenantId}
        editHref={`/issued/${encodeURIComponent(invoice.id)}/edit?${tenantQuery}`}
        copyAction={copyButton}
        downloads={downloads}
        deleteAction={
          <button type="button" className="ui-button ui-button--danger-outline ui-button--md" onClick={() => setDeleteOpen(true)}>
            <Icon name="trash-2" /> Usuń szkic
          </button>
        }
      />

      {accepted && verificationUrl ? (
        <section className="card kod-card" aria-labelledby="kod-title" data-testid="kod-card">
          <div className="kod-card__qr">
            <QRCodeSVG value={verificationUrl} size={200} level="M" marginSize={4} title="KOD I" />
          </div>
          <div>
            <h2 id="kod-title">KOD I</h2>
            <p className="inv-help">Kod jest na PDF. Nabywca bez KSeF sprawdzi nim fakturę.</p>
            {ksefNumber ? <p className="mono kod-card__number">{ksefNumber}</p> : null}
            <a href={verificationUrl} target="_blank" rel="noopener noreferrer">
              Otwórz weryfikację
            </a>
          </div>
        </section>
      ) : null}

      <div className="two">
        <section className="card" aria-labelledby="parties-title">
          <h2 id="parties-title">Strony i daty</h2>
          <dl className="kv">
            <dt>Sprzedawca</dt>
            <dd>
              <Party {...invoice.seller} />
            </dd>
            <dt>NIP sprzedawcy</dt>
            <dd className="mono">{invoice.seller.nip}</dd>
            <dt>Nabywca</dt>
            <dd>
              <Party {...invoice.buyer} />
            </dd>
            {invoice.buyer.nip ? (
              <>
                <dt>NIP nabywcy</dt>
                <dd className="mono">{invoice.buyer.nip}</dd>
              </>
            ) : null}
            {invoice.buyer.countryCode ? (
              <>
                <dt>Kraj nabywcy</dt>
                <dd>{countryName(invoice.buyer.countryCode)}</dd>
              </>
            ) : null}
            {invoice.buyer.foreignId ? (
              <>
                <dt>{invoice.buyer.idType === 'VatUe' ? 'Numer VAT UE' : 'Numer podatkowy'}</dt>
                <dd className="mono">{invoice.buyer.foreignId}</dd>
              </>
            ) : null}
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
                <dd className="mono">{invoice.seller.iban ? formatIban(invoice.seller.iban) : formatNrb(invoice.seller.bankAccount)}</dd>
                {invoice.seller.swift ? (
                  <>
                    <dt>SWIFT</dt>
                    <dd className="mono">{invoice.seller.swift}</dd>
                  </>
                ) : null}
                {invoice.seller.bankName ? (
                  <>
                    <dt>Bank</dt>
                    <dd>{invoice.seller.bankName}</dd>
                  </>
                ) : null}
              </>
            ) : null}
            {invoice.exchangeRate ? (
              <>
                <dt>Kurs</dt>
                <dd>
                  1 {invoice.currency} = {formatDecimal(invoice.exchangeRate, 2)} PLN
                  {invoice.exchangeRateDate ? ` (NBP z ${formatDate(invoice.exchangeRateDate)})` : ' (wpisany ręcznie)'}
                </dd>
              </>
            ) : null}
          </dl>
        </section>

        <div className="col">
          <section className="card" aria-labelledby="lines-title">
            <h2 id="lines-title">Pozycje</h2>
            <table className="lines">
              <thead>
                <tr>
                  <th scope="col">Ilość</th>
                  <th scope="col">Cena netto</th>
                  <th scope="col">Stawka</th>
                  <th scope="col">Netto</th>
                </tr>
              </thead>
              {invoice.lines.map((line) => (
                <tbody key={line.lineNo}>
                  <tr>
                    <th colSpan={4} scope="rowgroup">
                      {line.name}
                    </th>
                  </tr>
                  <tr>
                    <td>
                      {formatDecimal(line.quantity, 0)} {line.unit}
                    </td>
                    <td>{formatDecimal(line.unitNetPrice, 2)}</td>
                    <td>{vatRateLabel(line.vatRate)}</td>
                    <td>{formatMoney(line.netAmount, invoice.currency)}</td>
                  </tr>
                </tbody>
              ))}
            </table>
            {invoice.exemptionBasis ? <p className="inv-help">Podstawa zwolnienia: {invoice.exemptionBasis.text}</p> : null}
          </section>
          <VatSummary
            rows={invoice.vatSummary}
            totalNet={invoice.totalNet}
            totalVat={invoice.totalVat}
            totalGross={invoice.totalGross}
            currency={invoice.currency}
          />
        </div>
      </div>

      <ConfirmDialog
        open={deleteOpen}
        title="Usunąć szkic?"
        message="Szkic zostanie trwale usunięty. Tej operacji nie można cofnąć."
        confirmLabel="Usuń szkic"
        cancelLabel="Anuluj"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => remove.mutate()}
        onCancel={() => setDeleteOpen(false)}
      />
    </section>
  )
}

interface PartyProps {
  name: string | null
  addressLine1: string | null
  addressLine2: string | null
}

function Party({ name, addressLine1, addressLine2 }: PartyProps): ReactElement {
  return (
    <span className="party">
      {[name ?? '—', addressLine1, addressLine2].filter(Boolean).map((line) => (
        <span key={line}>{line}</span>
      ))}
    </span>
  )
}
