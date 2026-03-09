import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'

import { getInvoiceByKSeFNumber, getTransferDetails, setInvoicePaid } from '@/api/endpoints/invoices'
import { listTenants } from '@/api/endpoints/tenants'
import { ApiError } from '@/api/errors'
import { AsyncStateView } from '@/components/AsyncStateView'

export function InvoiceDetailsPage(): ReactElement {
  const { ksefInvoiceNumber = '' } = useParams()
  const [searchParams] = useSearchParams()
  const queryClient = useQueryClient()
  const [copySuccess, setCopySuccess] = useState(false)

  const tenantIdFromUrl = searchParams.get('tenantId') ?? ''

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'invoice-details'],
    queryFn: () => listTenants(),
  })

  const effectiveTenantId = tenantIdFromUrl || tenantsQuery.data?.[0]?.id || ''

  const invoiceQuery = useQuery({
    queryKey: ['invoice-details', effectiveTenantId, ksefInvoiceNumber],
    queryFn: () => getInvoiceByKSeFNumber(effectiveTenantId, ksefInvoiceNumber),
    enabled: Boolean(effectiveTenantId && ksefInvoiceNumber),
    retry: false,
  })

  const invoice = invoiceQuery.data

  const transferQuery = useQuery({
    queryKey: ['transfer-details', effectiveTenantId, invoice?.id],
    queryFn: () => getTransferDetails(effectiveTenantId, invoice!.id),
    enabled: Boolean(effectiveTenantId && invoice?.id),
  })

  const paidMutation = useMutation({
    mutationFn: (isPaid: boolean) => setInvoicePaid(effectiveTenantId, invoice!.id, isPaid),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invoice-details', effectiveTenantId, ksefInvoiceNumber] })
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
  })

  const isNotFound = invoiceQuery.error instanceof ApiError && invoiceQuery.error.status === 404
  const isLoading = tenantsQuery.isLoading || invoiceQuery.isLoading
  const hasData = Boolean(invoiceQuery.data)
  const shouldShowEmpty = !isLoading && (isNotFound || (!invoiceQuery.error && !hasData))

  const transfer = transferQuery.data

  function handleCopyTransferText(): void {
    if (!transfer) return
    void navigator.clipboard.writeText(transfer.transferText).then(() => {
      setCopySuccess(true)
      setTimeout(() => setCopySuccess(false), 2000)
    })
  }

  return (
    <section>
      <Link
        data-testid="invoice-details-back-link"
        to={tenantIdFromUrl ? `/invoices?tenantId=${encodeURIComponent(tenantIdFromUrl)}` : '/invoices'}
        className="back-link"
      >
        ← Powrót do faktur
      </Link>

      <header className="page-header">
        <h1>Szczegóły faktury</h1>
      </header>

      <AsyncStateView
        isLoading={isLoading}
        error={isNotFound ? null : invoiceQuery.error}
        isEmpty={shouldShowEmpty}
        loadingLines={5}
        emptyTitle="Nie znaleziono faktury"
        emptyMessage="Nie znaleziono żądanej faktury."
        onRetry={() => void invoiceQuery.refetch()}
      >
        {invoice ? (
          <>
            <div className="invoice-detail-card" data-testid="invoice-details-card">
              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Numer KSeF</span>
                <span
                  className="invoice-detail-value invoice-detail-value--mono"
                  data-testid="invoice-detail-ksef-number"
                >
                  {invoice.ksefInvoiceNumber}
                </span>
              </div>

              {invoice.invoiceNumber && (
                <div className="invoice-detail-row">
                  <span className="invoice-detail-label">Numer faktury</span>
                  <span
                    className="invoice-detail-value invoice-detail-value--mono"
                    data-testid="invoice-detail-invoice-number"
                  >
                    {invoice.invoiceNumber}
                  </span>
                </div>
              )}

              {invoice.invoiceType && (
                <div className="invoice-detail-row">
                  <span className="invoice-detail-label">Typ faktury</span>
                  <span className="invoice-detail-value" data-testid="invoice-detail-invoice-type">
                    {invoice.invoiceType.toUpperCase()}
                  </span>
                </div>
              )}

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Sprzedawca</span>
                <span className="invoice-detail-value" data-testid="invoice-detail-vendor-name">
                  {invoice.vendorName}
                </span>
              </div>

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">NIP sprzedawcy</span>
                <span
                  className="invoice-detail-value"
                  data-testid="invoice-detail-vendor-nip"
                  style={{ fontFamily: 'ui-monospace, monospace' }}
                >
                  {invoice.vendorNip}
                </span>
              </div>

              {invoice.buyerName && (
                <div className="invoice-detail-row">
                  <span className="invoice-detail-label">Nabywca</span>
                  <span className="invoice-detail-value" data-testid="invoice-detail-buyer-name">
                    {invoice.buyerName}
                  </span>
                </div>
              )}

              {invoice.buyerNip && (
                <div className="invoice-detail-row">
                  <span className="invoice-detail-label">NIP nabywcy</span>
                  <span
                    className="invoice-detail-value"
                    data-testid="invoice-detail-buyer-nip"
                    style={{ fontFamily: 'ui-monospace, monospace' }}
                  >
                    {invoice.buyerNip}
                  </span>
                </div>
              )}

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Data wystawienia</span>
                <span className="invoice-detail-value" data-testid="invoice-detail-issue-date">
                  {new Date(invoice.issueDate).toLocaleDateString('pl-PL')}
                </span>
              </div>

              {invoice.acquisitionDate && (
                <div className="invoice-detail-row">
                  <span className="invoice-detail-label">Data przyjęcia w KSeF</span>
                  <span className="invoice-detail-value" data-testid="invoice-detail-acquisition-date">
                    {new Date(invoice.acquisitionDate).toLocaleDateString('pl-PL')}
                  </span>
                </div>
              )}

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Kwota netto</span>
                <span className="invoice-detail-value" data-testid="invoice-detail-amount-net">
                  {invoice.amountNet.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {invoice.currency}
                </span>
              </div>

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">VAT</span>
                <span className="invoice-detail-value" data-testid="invoice-detail-amount-vat">
                  {invoice.amountVat.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {invoice.currency}
                </span>
              </div>

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Kwota brutto</span>
                <span
                  className="invoice-detail-value invoice-detail-value--amount"
                  data-testid="invoice-detail-amount"
                >
                  {invoice.amountGross.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {invoice.currency}
                </span>
              </div>

              <div className="invoice-detail-row">
                <span className="invoice-detail-label">Status płatności</span>
                <span className="invoice-detail-value" data-testid="invoice-detail-paid-status">
                  <span className={`payment-status-badge ${invoice.isPaid ? 'payment-status-badge--paid' : 'payment-status-badge--unpaid'}`}>
                    {invoice.isPaid ? 'Opłacona' : 'Nieopłacona'}
                  </span>
                  {invoice.isPaid && invoice.paidAt && (
                    <span className="payment-status-date">
                      {new Date(invoice.paidAt).toLocaleDateString('pl-PL')}
                    </span>
                  )}
                </span>
              </div>
            </div>

            <div className="payment-actions" data-testid="payment-actions">
              <button
                className={`ui-button ${invoice.isPaid ? 'ui-button--secondary' : 'ui-button--primary'} mark-paid-btn`}
                data-testid="toggle-paid-btn"
                disabled={paidMutation.isPending}
                onClick={() => paidMutation.mutate(!invoice.isPaid)}
              >
                {paidMutation.isPending
                  ? 'Zapisywanie...'
                  : invoice.isPaid
                    ? 'Cofnij oznaczenie'
                    : 'Oznacz jako opłaconą'}
              </button>
              {paidMutation.isError && (
                <span className="payment-actions-error">Nie udało się zmienić statusu płatności.</span>
              )}
            </div>

            <div className="transfer-details-card" data-testid="transfer-details">
              <h2 className="transfer-details-title">Dane do przelewu</h2>
              {transferQuery.isLoading ? (
                <p className="text-muted">Ładowanie danych przelewu...</p>
              ) : transfer ? (
                <>
                  <div className="invoice-detail-row">
                    <span className="invoice-detail-label">Odbiorca</span>
                    <span className="invoice-detail-value" data-testid="transfer-recipient">
                      {transfer.recipientName}
                    </span>
                  </div>
                  <div className="invoice-detail-row">
                    <span className="invoice-detail-label">NIP</span>
                    <span className="invoice-detail-value" data-testid="transfer-nip" style={{ fontFamily: 'ui-monospace, monospace' }}>
                      {transfer.recipientNip}
                    </span>
                  </div>
                  <div className="invoice-detail-row">
                    <span className="invoice-detail-label">Nr rachunku</span>
                    <span className="invoice-detail-value" data-testid="transfer-account" style={{ fontFamily: 'ui-monospace, monospace' }}>
                      {transfer.recipientAccount ?? 'brak'}
                    </span>
                  </div>
                  <div className="invoice-detail-row">
                    <span className="invoice-detail-label">Kwota</span>
                    <span className="invoice-detail-value invoice-detail-value--amount" data-testid="transfer-amount">
                      {transfer.amount.toLocaleString('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} {transfer.currency}
                    </span>
                  </div>
                  <div className="invoice-detail-row">
                    <span className="invoice-detail-label">Tytuł przelewu</span>
                    <span className="invoice-detail-value" data-testid="transfer-title">
                      {transfer.title}
                    </span>
                  </div>

                  <div className="transfer-copy-section">
                    <button
                      className="ui-button ui-button--secondary"
                      data-testid="copy-transfer-btn"
                      onClick={handleCopyTransferText}
                    >
                      {copySuccess ? 'Skopiowano!' : 'Kopiuj dane przelewu'}
                    </button>
                  </div>

                  <div className="qr-code-section" data-testid="qr-code-section">
                    <h3 className="qr-code-title">Kod QR do przelewu</h3>
                    <img
                      src={transfer.qrCodeBase64}
                      alt="Kod QR do przelewu"
                      className="qr-code-image"
                      data-testid="qr-code-image"
                    />
                  </div>
                </>
              ) : transferQuery.isError ? (
                <p className="text-error">Nie udało się załadować danych przelewu.</p>
              ) : null}
            </div>
          </>
        ) : null}
      </AsyncStateView>
    </section>
  )
}
