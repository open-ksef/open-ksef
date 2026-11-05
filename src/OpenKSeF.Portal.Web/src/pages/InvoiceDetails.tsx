import { useMutation, useMutationState, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'

import { getInvoiceByKSeFNumber, getTransferDetails, setInvoicePaid } from '@/api/endpoints/invoices'
import { ApiError } from '@/api/errors'
import type { InvoiceResponse, PagedResult, TransferDetailsResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { DueDate } from '@/components/DueDate'
import { Icon } from '@/components/Icon'
import { PaidStatus } from '@/components/PaidStatus'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import { accountDigits, formatDate, formatDecimal, formatMoney, formatNrb, formatWarsawDate } from '@/invoicing/format'
import { vatRateLabel } from '@/invoicing/totals'

const PAID_MUTATION_KEY = ['invoice-paid']

function amountLabel(invoice: InvoiceResponse): string {
  if (invoice.isPaid) return 'Kwota faktury'
  const type = invoice.invoiceType?.toLowerCase() ?? ''
  if (type === 'zal' || type === 'roz') return 'Kwota faktury'
  if (type.startsWith('kor')) return 'Kwota brutto (różnica)'
  if (!type.trim()) return 'Kwota brutto'
  return 'Do zapłaty'
}

function buyerId(invoice: InvoiceResponse): { label: string; value: string } | null {
  const type = invoice.buyerIdType
  if (!type || type === 'Nip') return invoice.buyerNip ? { label: 'NIP nabywcy', value: invoice.buyerNip } : null
  return invoice.buyerForeignId ? { label: type === 'VatUe' ? 'VAT UE' : 'Nr podatkowy', value: invoice.buyerForeignId } : null
}

function PaymentCard({
  invoice,
  transfer,
  isLoading,
  isError,
  isSaving,
  onTogglePaid,
}: {
  invoice: InvoiceResponse
  transfer: TransferDetailsResponse | undefined
  isLoading: boolean
  isError: boolean
  isSaving: boolean
  onTogglePaid: () => void
}): ReactElement {
  const [copied, setCopied] = useState<{ id: string; what: 'transfer' | 'account' } | null>(null)
  const accountOnly = accountDigits(transfer?.recipientAccount)

  async function handleCopy(text: string, what: 'transfer' | 'account'): Promise<void> {
    try {
      await navigator.clipboard.writeText(text)
      setCopied({ id: invoice.id, what })
      setTimeout(() => setCopied(null), 2000)
    } catch {
      setCopied(null)
      toast.error(what === 'account' ? 'Nie udało się skopiować numeru rachunku.' : 'Nie udało się skopiować danych przelewu.')
    }
  }

  const hasQr = Boolean(transfer?.qrCodeBase64)
  const title = invoice.isPaid ? 'Opłacona' : hasQr ? 'Zapłać tę fakturę' : 'Dane do przelewu'

  const amount = transfer ? (
    <>
      <span className="pay-caption" data-testid="pay-amount-label">
        {amountLabel(invoice)}
      </span>
      <div className="pay-amount" data-testid="transfer-amount">
        {formatMoney(transfer.amount, transfer.currency)}
      </div>
    </>
  ) : null

  const transferData = transfer ? (
    <>
      <div className={hasQr ? 'pay-grid' : 'pay-grid pay-grid--one'}>
        {transfer.qrCodeBase64 ? (
          <div className="pay-qr" data-testid="qr-code-section">
            <img key={invoice.id} src={transfer.qrCodeBase64} alt="Kod QR do przelewu" width={200} height={200} data-testid="qr-code-image" />
          </div>
        ) : null}
        <div>
          {invoice.isPaid ? null : amount}
          <dl className="kv">
            <dt>Termin płatności</dt>
            <dd>
              <DueDate row={invoice} testId="transfer-due-date" />
            </dd>
            <dt>Odbiorca</dt>
            <dd data-testid="transfer-recipient">{transfer.recipientName}</dd>
            <dt>Tytuł</dt>
            <dd data-testid="transfer-title">{transfer.title}</dd>
          </dl>
        </div>
        <div className="pay-full">
          <span className="pay-caption">Rachunek odbiorcy</span>
          <div className="pay-nrb" data-testid="transfer-account">
            {transfer.recipientAccount ? formatNrb(transfer.recipientAccount) : 'brak'}
          </div>
          {accountOnly ? (
            <Button variant="outline" size="sm" className="pay-copy-account" data-testid="copy-account-btn" onClick={() => void handleCopy(accountOnly, 'account')}>
              <Icon name="copy" size={14} />
              {copied?.id === invoice.id && copied.what === 'account' ? 'Skopiowano!' : 'Kopiuj numer rachunku'}
            </Button>
          ) : null}
        </div>
      </div>
      {transfer.qrMessage ? (
        <div className="inv-alert inv-alert--warning pay-notice" role="status">
          <Icon name="triangle-alert" />
          <span>
            <span data-testid="qr-message">{transfer.qrMessage}</span>
            {!hasQr && !invoice.invoiceType?.trim() ? (
              <>
                {' '}
                <Link to="/company/token" data-testid="pay-fetch-link">
                  Przejdź do pobierania
                </Link>
              </>
            ) : null}
          </span>
        </div>
      ) : null}
    </>
  ) : null

  const copyButton = transfer ? (
    <Button variant="outline" data-testid="copy-transfer-btn" onClick={() => void handleCopy(transfer.transferText, 'transfer')}>
      <Icon name="copy" />
      {copied?.id === invoice.id && copied.what === 'transfer' ? 'Skopiowano!' : 'Kopiuj dane przelewu'}
    </Button>
  ) : null

  const markButton = (
    <Button
      variant={invoice.isPaid ? 'outline' : 'primary'}
      data-testid="toggle-paid-btn"
      disabled={isSaving}
      onClick={onTogglePaid}
    >
      {invoice.isPaid ? null : <Icon name="check" />}
      {isSaving ? 'Zapisywanie...' : invoice.isPaid ? 'Cofnij oznaczenie' : 'Oznacz jako opłaconą'}
    </Button>
  )

  return (
    <section className="card" aria-labelledby="pay-title" data-testid="transfer-details">
      <h2 id="pay-title" data-testid="pay-title">
        {title}
      </h2>
      {isLoading ? <p className="pay-state">Ładowanie danych przelewu...</p> : null}
      {isError ? (
        <p className="pay-state pay-state--error" role="alert">
          Nie udało się załadować danych przelewu.
        </p>
      ) : null}
      {invoice.isPaid ? (
        <>
          {amount}
          <p className="pay-paid-note">
            {invoice.paidAt ? <span data-testid="invoice-detail-paid-date">{formatWarsawDate(invoice.paidAt)} </span> : null}
            <span>Oznaczona w OpenKSeF. To nie jest potwierdzenie przelewu.</span>
          </p>
          <div className="pay-actions">{markButton}</div>
          {transfer ? (
            <details key={invoice.id} className="pay-details" data-testid="pay-collapsed">
              <summary>Pokaż dane do przelewu</summary>
              {transferData}
              <div className="pay-actions">{copyButton}</div>
            </details>
          ) : null}
        </>
      ) : (
        <>
          {transferData}
          {hasQr ? <p className="pay-help">Zeskanuj kod aplikacją banku w telefonie, a potem oznacz fakturę.</p> : null}
          <div className="pay-actions">
            {markButton}
            {copyButton}
          </div>
        </>
      )}
    </section>
  )
}

export function InvoiceDetailsPage(): ReactElement {
  const { ksefInvoiceNumber = '' } = useParams()
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const queryClient = useQueryClient()

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''
  const walkState = location.state as { listSearch?: string; siblings?: string[]; fromIssued?: boolean } | null
  const listSearch = walkState?.listSearch
  const siblings = walkState?.siblings ?? []
  const position = siblings.indexOf(ksefInvoiceNumber)
  const previous = position > 0 ? siblings[position - 1] : undefined
  const next = position >= 0 ? siblings[position + 1] : undefined

  const previousLink = useRef<HTMLAnchorElement>(null)
  const nextLink = useRef<HTMLAnchorElement>(null)
  const walked = useRef(false)

  useEffect(() => {
    if (walked.current && document.activeElement === document.body) {
      ;(previousLink.current ?? nextLink.current)?.focus()
    }
    walked.current = false
  }, [ksefInvoiceNumber])

  const tenantsQuery = useTenants()
  const { tenantId: effectiveTenantId } = useCompany()

  const invoiceQuery = useQuery({
    queryKey: ['invoice-details', effectiveTenantId, ksefInvoiceNumber],
    queryFn: () => getInvoiceByKSeFNumber(effectiveTenantId, ksefInvoiceNumber),
    enabled: Boolean(effectiveTenantId && ksefInvoiceNumber),
    retry: false,
  })

  const invoice = invoiceQuery.data
  const isIssued = invoice?.direction === 'Issued'
  const backToIssued = isIssued || walkState?.fromIssued === true
  const listPath = backToIssued ? '/issued' : '/invoices'
  const buyer = invoice ? buyerId(invoice) : null

  const transferQuery = useQuery({
    queryKey: ['transfer-details', effectiveTenantId, invoice?.id],
    queryFn: () => getTransferDetails(effectiveTenantId, invoice!.id),
    enabled: Boolean(effectiveTenantId && invoice?.id && !isIssued),
  })

  const paidMutation = useMutation({
    mutationKey: PAID_MUTATION_KEY,
    mutationFn: ({ tenantId, invoiceId, isPaid }: { tenantId: string; invoiceId: string; isPaid: boolean }) =>
      setInvoicePaid(tenantId, invoiceId, isPaid),
    onSuccess: (data, vars) => {
      queryClient.setQueriesData<PagedResult<InvoiceResponse>>(
        { queryKey: ['invoices', vars.tenantId] },
        (page) =>
          page && {
            ...page,
            items: page.items.map((row) => (row.id === data.id ? { ...row, isPaid: data.isPaid, paidAt: data.paidAt } : row)),
          },
      )
      queryClient.setQueryData(['invoice-details', vars.tenantId, data.ksefInvoiceNumber], data)
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
    onError: () => {
      toast.error('Nie udało się zmienić statusu płatności.')
    },
  })

  const savingIds = useMutationState({
    filters: { mutationKey: PAID_MUTATION_KEY, status: 'pending' },
    select: (running) => (running.state.variables as { invoiceId: string }).invoiceId,
  })

  const isNotFound = invoiceQuery.error instanceof ApiError && invoiceQuery.error.status === 404
  const isLoading = tenantsQuery.isLoading || invoiceQuery.isLoading
  const hasData = Boolean(invoiceQuery.data)
  const companiesFailed = tenantsQuery.isError && !effectiveTenantId
  const shouldShowEmpty = !isLoading && !companiesFailed && (isNotFound || (!invoiceQuery.error && !hasData))
  const loadError = companiesFailed
    ? new Error('Nie udało się pobrać listy firm. Spróbuj ponownie za chwilę.')
    : isNotFound || !invoiceQuery.error
      ? null
      : new Error('Nie udało się pobrać faktury.')

  const walkTo = (number: string) => ({ pathname: `/invoices/${encodeURIComponent(number)}`, search: location.search })
  const onWalk = (): void => {
    walked.current = true
  }

  return (
    <section className="invoice-details">
      <Link
        data-testid="invoice-details-back-link"
        to={listSearch !== undefined ? `${listPath}${listSearch}` : tenantIdFromUrl ? `${listPath}?tenantId=${encodeURIComponent(tenantIdFromUrl)}` : listPath}
        className="back-link"
      >
        <Icon name="arrow-left" size={14} />
        {backToIssued ? 'Wystawione' : 'Otrzymane'}
      </Link>

      <header className="page-header invoice-details-head">
        <h1 data-testid="invoice-detail-title">{invoice?.invoiceNumber ?? ksefInvoiceNumber}</h1>
        {isIssued ? (
          <span className="inv-badge inv-badge--neutral" data-testid="invoice-detail-direction">
            Wystawiona
          </span>
        ) : invoice ? (
          <PaidStatus row={invoice} testId="invoice-detail-paid-status" />
        ) : null}
        <div className="invoice-walk">
          {previous !== undefined ? (
            <Link
              ref={previousLink}
              to={walkTo(previous)}
              state={walkState}
              replace
              onClick={onWalk}
              className="ui-button ui-button--outline ui-button--sm"
              aria-label="Poprzednia faktura"
              data-testid="invoice-prev"
            >
              Poprzednia
            </Link>
          ) : null}
          {next !== undefined ? (
            <Link
              ref={nextLink}
              to={walkTo(next)}
              state={walkState}
              replace
              onClick={onWalk}
              className="ui-button ui-button--outline ui-button--sm"
              aria-label="Następna faktura"
              data-testid="invoice-next"
            >
              Następna
            </Link>
          ) : null}
        </div>
      </header>

      <AsyncStateView
        isLoading={isLoading}
        error={loadError}
        isEmpty={shouldShowEmpty}
        loadingLines={5}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Nie znaleziono żądanej faktury."
        onRetry={() => void (companiesFailed ? tenantsQuery.refetch() : invoiceQuery.refetch())}
      >
        {invoice ? (
          <div className="two">
            {isIssued ? null : (
              <PaymentCard
                invoice={invoice}
                transfer={transferQuery.data}
                isLoading={transferQuery.isLoading}
                isError={transferQuery.isError}
                isSaving={savingIds.includes(invoice.id)}
                onTogglePaid={() =>
                  paidMutation.mutate({ tenantId: effectiveTenantId, invoiceId: invoice.id, isPaid: !invoice.isPaid })
                }
              />
            )}

            <div className={isIssued ? 'col col--flat' : 'col'}>
              <section className="card" aria-labelledby="lines-title" data-testid="invoice-lines">
                <h2 id="lines-title">Pozycje</h2>
                {invoice.lines?.length ? (
                  <table className="lines">
                    <thead>
                      <tr>
                        <th scope="col">Ilość</th>
                        <th scope="col">Cena netto</th>
                        <th scope="col">Stawka</th>
                        <th scope="col">Brutto</th>
                      </tr>
                    </thead>
                    {invoice.lines.map((line) => (
                      <tbody key={line.lineNumber}>
                        <tr>
                          <th colSpan={4} scope="rowgroup">
                            {line.name ?? '—'}
                          </th>
                        </tr>
                        <tr>
                          <td>
                            {line.quantity != null ? formatDecimal(line.quantity, 0) : '—'}
                            {line.unit ? ` ${line.unit}` : ''}
                          </td>
                          <td>{line.unitPriceNet != null ? formatDecimal(line.unitPriceNet, 2) : '—'}</td>
                          <td>{line.vatRate ? vatRateLabel(line.vatRate) : '—'}</td>
                          <td>{line.amountGross != null ? formatMoney(line.amountGross, invoice.currency) : '—'}</td>
                        </tr>
                      </tbody>
                    ))}
                  </table>
                ) : (
                  <p className="pay-state" data-testid="invoice-lines-empty">
                    {isIssued ? 'Pozycji tej faktury nie pobrano jeszcze z KSeF.' : 'Brak pozycji w pobranych danych.'}
                  </p>
                )}
              </section>

              <section className="card" aria-labelledby="data-title" data-testid="invoice-details-card">
                <h2 id="data-title">Dane faktury</h2>
                <dl className="kv">
                  <dt>Sprzedawca</dt>
                  <dd data-testid="invoice-detail-vendor-name">{invoice.vendorName}</dd>
                  <dt>NIP sprzedawcy</dt>
                  <dd className="mono" data-testid="invoice-detail-vendor-nip">
                    {invoice.vendorNip}
                  </dd>
                  {invoice.buyerName ? (
                    <>
                      <dt>Nabywca</dt>
                      <dd data-testid="invoice-detail-buyer-name">{invoice.buyerName}</dd>
                    </>
                  ) : null}
                  {buyer ? (
                    <>
                      <dt data-testid="invoice-detail-buyer-id-label">{buyer.label}</dt>
                      <dd className="mono" data-testid="invoice-detail-buyer-nip">
                        {buyer.value}
                      </dd>
                    </>
                  ) : null}
                  {invoice.invoiceNumber ? (
                    <>
                      <dt>Numer faktury</dt>
                      <dd data-testid="invoice-detail-invoice-number">{invoice.invoiceNumber}</dd>
                    </>
                  ) : null}
                  {invoice.invoiceType ? (
                    <>
                      <dt>Typ faktury</dt>
                      <dd data-testid="invoice-detail-invoice-type">{invoice.invoiceType.toUpperCase()}</dd>
                    </>
                  ) : null}
                  <dt>Data wystawienia</dt>
                  <dd data-testid="invoice-detail-issue-date">{formatDate(invoice.issueDate)}</dd>
                  {isIssued ? null : (
                    <>
                      <dt>Termin płatności</dt>
                      <dd>
                        <DueDate row={invoice} testId="invoice-detail-due-date" />
                      </dd>
                    </>
                  )}
                  {invoice.acquisitionDate ? (
                    <>
                      <dt>Data przyjęcia w KSeF</dt>
                      <dd data-testid="invoice-detail-acquisition-date">{formatWarsawDate(invoice.acquisitionDate)}</dd>
                    </>
                  ) : null}
                  <dt>Netto</dt>
                  <dd data-testid="invoice-detail-amount-net">{formatMoney(invoice.amountNet, invoice.currency)}</dd>
                  <dt data-testid="invoice-detail-vat-label">{invoice.currency === 'PLN' ? 'VAT' : 'VAT w przeliczeniu na PLN'}</dt>
                  <dd data-testid="invoice-detail-amount-vat">{formatMoney(invoice.amountVat, 'PLN')}</dd>
                  <dt>Brutto</dt>
                  <dd data-testid="invoice-detail-amount">{formatMoney(invoice.amountGross, invoice.currency)}</dd>
                  <dt>Numer KSeF</dt>
                  <dd className="mono" data-testid="invoice-detail-ksef-number">
                    {invoice.ksefInvoiceNumber}
                  </dd>
                </dl>
              </section>
            </div>
          </div>
        ) : null}
      </AsyncStateView>
    </section>
  )
}
