import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { useState, type ReactElement } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'

import {
  copyIssuedInvoice,
  createAdvance,
  createCorrection,
  createFinal,
  deleteIssuedInvoice,
  getIssuedInvoice,
  getIssuedInvoiceUpo,
  getIssuedInvoiceXml,
  reissueInvoice,
  saveAsTemplate,
} from '@/api/endpoints/issuedInvoices'
import type { CorrectionMode, IssuedInvoiceLineResponse, IssuedInvoiceResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { EnvBadge, KsefStatusBadge } from '@/components/invoicing/KsefStatusBadge'
import { ChainCard } from '@/components/invoicing/ChainCard'
import { CorrectionRows } from '@/components/invoicing/CorrectionRows'
import { KsefStatusPanel } from '@/components/invoicing/KsefStatusPanel'
import { SaveTemplateButton } from '@/components/invoicing/SaveTemplateButton'
import { VatSummary } from '@/components/invoicing/VatSummary'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import { isChainDraft, KIND_LABELS } from '@/invoicing/advance'
import { renderBilingualPdf, type PrintVariant } from '@/invoicing/bilingualPrint'
import { CORRECTION_TYPES, isCorrection } from '@/invoicing/correction'
import { countryName } from '@/invoicing/foreign'
import { formatDate, formatDecimal, formatIban, formatMoney, formatNrb, formatSignedMoney } from '@/invoicing/format'
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
    <div className="inv-stack">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować faktury'}
          onRetry={() => void invoiceQuery.refetch()}
        />
      ) : !invoiceQuery.data ? (
        <Skeleton variant="card" lines={8} label="Wczytywanie faktury…" />
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
  const correction = isCorrection(invoice)
  const kind = invoice.invoiceType ?? 'VAT'
  const advance = kind === 'ZAL'
  const advanceAfter = advance
    ? (invoice.corrections ?? []).filter((item) => item.status === 'Accepted').reduce((sum, item) => sum + item.totalGross, invoice.totalGross)
    : null
  const noPayment = advance || ((correction || kind === 'ROZ') && invoice.totalGross <= 0)
  const chain = invoice.chain
  const chainOpen = Boolean(chain?.openDocumentId)
  const canAdvanceAgain = accepted && advance && !chain?.final && (chain?.remaining ?? 1) > 0
  const canFinal = accepted && advance && !chain?.final
  const correctable = accepted && !correction && !invoice.currentState?.isZero
  const openCorrection = invoice.corrections?.find((item) => item.id === invoice.openCorrectionId)
  const [wrongNipOpen, setWrongNipOpen] = useState(false)
  const editHref = `/issued/${encodeURIComponent(invoice.id)}/${correction ? 'correct' : isChainDraft(invoice) ? 'chain' : 'edit'}?${tenantQuery}`
  const itemHref = (id: string): string => `/issued/${encodeURIComponent(id)}?${tenantQuery}`

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

  const correct = useMutation({
    mutationFn: (mode: CorrectionMode) => createCorrection(tenantId, invoice.id, mode),
    onMutate: () => setFailure(null),
    onSuccess: (draft) => {
      setWrongNipOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, invoice.id] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/correct?${tenantQuery}`)
    },
    onError: (error) => {
      setWrongNipOpen(false)
      showFailure(error, 'Nie udało się utworzyć korekty')
    },
  })

  const chainStep = useMutation({
    mutationFn: (step: 'advance' | 'final') => (step === 'advance' ? createAdvance(tenantId, invoice.id) : createFinal(tenantId, invoice.id)),
    onMutate: () => setFailure(null),
    onSuccess: (draft) => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      void queryClient.invalidateQueries({ queryKey: ['issued-invoice', tenantId, invoice.id] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/chain?${tenantQuery}`)
    },
    onError: (error) => showFailure(error, 'Nie udało się utworzyć dokumentu zamówienia'),
  })

  const reissue = useMutation({
    mutationFn: () => reissueInvoice(tenantId, invoice.id),
    onMutate: () => setFailure(null),
    onSuccess: (draft) => {
      void queryClient.invalidateQueries({ queryKey: ['issued-invoices', tenantId] })
      navigate(`/issued/${encodeURIComponent(draft.id)}/edit?${tenantQuery}`)
    },
    onError: (error) => showFailure(error, 'Nie udało się utworzyć faktury'),
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
        <h1>
          {kind !== 'VAT' ? <span className="inv-caption">{KIND_LABELS[kind]} </span> : null}
          {invoice.number ?? 'Szkic'}
        </h1>
        <KsefStatusBadge status={invoice.status} />
        <EnvBadge environment={invoice.ksef.environment} />
        {kind === 'VAT' ? <SaveTemplateButton defaultName={invoice.buyer.name ?? ''} save={(name) => saveAsTemplate(tenantId, invoice.id, name)} /> : null}
      </header>

      {failure ? <ErrorBanner message={failure} /> : null}

      <KsefStatusPanel
        invoice={invoice}
        tenantId={tenantId}
        editHref={editHref}
        copyAction={kind === 'VAT' ? copyButton : null}
        downloads={downloads}
        deleteAction={
          <button type="button" className="ui-button ui-button--danger-outline ui-button--md" onClick={() => setDeleteOpen(true)}>
            <Icon name="trash-2" /> Usuń szkic
          </button>
        }
      />

      {chain ? (
        <ChainCard chain={chain} currency={invoice.currency} currentId={invoice.id} itemHref={itemHref}>
          {chainOpen && chain.openDocumentId !== invoice.id && (canAdvanceAgain || canFinal) ? (
            <div className="inv-alert inv-alert--info" role="status" data-testid="chain-open">
              Zamówienie ma otwarty dokument. Dokończ go albo usuń szkic. <Link to={itemHref(chain.openDocumentId ?? '')}>Otwórz dokument</Link>
            </div>
          ) : null}
          {canAdvanceAgain || canFinal ? (
            <div className="status-panel__row">
              {canAdvanceAgain ? (
                <button type="button" className="ui-button ui-button--primary ui-button--md" disabled={chainStep.isPending || chainOpen} onClick={() => chainStep.mutate('advance')}>
                  <Icon name="plus" /> Kolejna zaliczka
                </button>
              ) : null}
              {canFinal ? (
                <button type="button" className="ui-button ui-button--outline ui-button--md" disabled={chainStep.isPending || chainOpen} onClick={() => chainStep.mutate('final')}>
                  Wystaw fakturę rozliczającą
                </button>
              ) : null}
            </div>
          ) : null}
        </ChainCard>
      ) : null}

      {invoice.advanceInvoices?.length ? (
        <section className="card" aria-labelledby="advances-title" data-testid="advance-invoices">
          <h2 id="advances-title">Rozliczone faktury zaliczkowe</h2>
          <ul className="inv-kor-list">
            {invoice.advanceInvoices.map((item) => (
              <li key={item.id}>
                <Link to={itemHref(item.id)} className="mono">
                  {item.number ?? '—'}
                </Link>
                {item.ksefNumber ? <span className="mono"> · {item.ksefNumber}</span> : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {correction && invoice.correctedInvoice ? (
        <section className="card" aria-labelledby="corrected-title" data-testid="corrected-invoice">
          <h2 id="corrected-title">Korygowana faktura</h2>
          <dl className="kv">
            <dt>Faktura</dt>
            <dd>
              <Link to={itemHref(invoice.correctedInvoice.id)} className="mono">
                {invoice.correctedInvoice.number ?? '—'}
              </Link>{' '}
              z {formatDate(invoice.correctedInvoice.issueDate)}
            </dd>
            {invoice.correctedInvoice.ksefNumber ? (
              <>
                <dt>Numer KSeF</dt>
                <dd className="mono">{invoice.correctedInvoice.ksefNumber}</dd>
              </>
            ) : null}
            <dt>Przyczyna</dt>
            <dd>{invoice.correctionReason || '—'}</dd>
            {invoice.correctionType ? (
              <>
                <dt>Typ korekty</dt>
                <dd>{CORRECTION_TYPES.find((type) => type.value === invoice.correctionType)?.label}</dd>
              </>
            ) : null}
            {invoice.amountBefore != null ? (
              <>
                <dt>{kind === 'KOR_ROZ' ? 'Do zapłaty przed korektą' : 'Zaliczka przed korektą'}</dt>
                <dd data-testid="amount-before">{formatMoney(invoice.amountBefore, invoice.currency)}</dd>
              </>
            ) : null}
            {invoice.buyerBefore ? (
              <>
                <dt>Nabywca przed korektą</dt>
                <dd>
                  <Party {...invoice.buyerBefore} />
                </dd>
              </>
            ) : null}
          </dl>
          <p className="inv-help">Kolejną korektę zrobisz z faktury pierwotnej: „Koryguj” poprawia jej aktualny stan.</p>
        </section>
      ) : null}

      {accepted && !correction ? (
        <section className="card" aria-labelledby="corrections-title" data-testid="corrections-card">
          <h2 id="corrections-title">Korekty</h2>
          {invoice.openCorrectionId ? (
            <div className="inv-alert inv-alert--info" role="status" data-testid="open-correction">
              Ta faktura ma otwartą korektę{openCorrection?.number ? <span className="mono"> {openCorrection.number}</span> : null}. Dokończ ją albo usuń szkic,
              zanim zaczniesz kolejną. <Link to={itemHref(invoice.openCorrectionId)}>Otwórz korektę</Link>
            </div>
          ) : null}
          {correctable ? (
            <div className="status-panel__row">
              <button
                type="button"
                className="ui-button ui-button--primary ui-button--md"
                disabled={correct.isPending || Boolean(invoice.openCorrectionId)}
                onClick={() => correct.mutate('Edit')}
              >
                <Icon name="pencil" /> Koryguj
              </button>
              <button
                type="button"
                className="ui-button ui-button--outline ui-button--md"
                disabled={correct.isPending || Boolean(invoice.openCorrectionId)}
                onClick={() => correct.mutate('ToZero')}
              >
                Koryguj do zera
              </button>
              {invoice.buyer.nip || invoice.buyer.foreignId ? (
                <button
                  type="button"
                  className="ui-button ui-button--outline ui-button--md"
                  disabled={correct.isPending || Boolean(invoice.openCorrectionId)}
                  onClick={() => setWrongNipOpen(true)}
                >
                  Błędny NIP nabywcy
                </button>
              ) : null}
            </div>
          ) : null}
          {invoice.currentState?.isZero && kind === 'VAT' ? (
            <div data-testid="reissue">
              <p className="inv-help">
                Faktura jest skorygowana do zera. „Wystaw poprawną fakturę” utworzy szkic z pozycjami sprzed tej korekty. Po korekcie „Błędny NIP nabywcy” pole
                NIP będzie puste: wpisz poprawny NIP.
              </p>
              <button type="button" className="ui-button ui-button--primary ui-button--md" disabled={reissue.isPending} onClick={() => reissue.mutate()}>
                {reissue.isPending ? 'Tworzenie…' : 'Wystaw poprawną fakturę'}
              </button>
            </div>
          ) : null}
          {invoice.corrections?.length ? (
            <ul className="inv-kor-list" aria-label="Korekty tej faktury">
              {invoice.corrections.map((item) => (
                <li key={item.id}>
                  <Link to={itemHref(item.id)} className="mono">
                    {item.number ?? 'Szkic korekty'}
                  </Link>{' '}
                  {formatDate(item.issueDate)} · <KsefStatusBadge status={item.status} /> · {formatSignedMoney(item.totalGross, invoice.currency)}
                </li>
              ))}
            </ul>
          ) : (
            <p className="inv-help">Brak korekt.</p>
          )}
        </section>
      ) : null}

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
            <dt>{advance ? 'Data otrzymania zapłaty' : 'Data sprzedaży'}</dt>
            <dd>{formatDate(invoice.saleDate)}</dd>
            {advance ? (
              <>
                <dt>Zapłacono</dt>
                <dd data-testid="advance-paid">
                  {formatMoney(invoice.advanceAmount ?? invoice.totalGross, invoice.currency)}, {paymentLabels[invoice.paymentMethod].toLowerCase()}
                </dd>
              </>
            ) : null}
            {noPayment ? null : (
              <>
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
              </>
            )}
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
          {correction ? (
            <section className="card" aria-labelledby="lines-title">
              <h2 id="lines-title">Pozycje przed i po korekcie</h2>
              <CorrectionRows rows={invoice.correctionRows ?? []} currency={invoice.currency} />
              <p className="inv-help">Kwoty w podsumowaniu to różnice wobec stanu przed korektą.</p>
            </section>
          ) : (
            <section className="card" aria-labelledby="lines-title">
              <h2 id="lines-title">{advance ? 'Zamówienie' : 'Pozycje'}</h2>
              <Lines lines={invoice.lines} currency={invoice.currency} />
              {kind === 'ROZ' ? <p className="inv-help">Pozycje z pełnymi wartościami. Podsumowanie pokazuje kwoty po odjęciu zaliczek.</p> : null}
              {advance ? <p className="inv-help">Podsumowanie pokazuje zaliczkę rozbitą na stawki.</p> : null}
              {invoice.exemptionBasis ? <p className="inv-help">Podstawa zwolnienia: {invoice.exemptionBasis.text}</p> : null}
            </section>
          )}
          <VatSummary
            rows={invoice.vatSummary}
            totalNet={invoice.totalNet}
            totalVat={invoice.totalVat}
            totalGross={invoice.totalGross}
            currency={invoice.currency}
            totalLabel={advance ? 'Zapłacono' : kind === 'KOR_ZAL' ? 'Zaliczka po korekcie' : undefined}
            payable={kind === 'KOR_ZAL' ? (invoice.advanceAmount ?? undefined) : undefined}
          />
          {invoice.currentState ? (
            <section className="card" aria-labelledby="current-state-title" data-testid="current-state">
              <h2 id="current-state-title">Stan po korektach</h2>
              {invoice.currentState.isZero ? <p className="inv-help">Faktura skorygowana do zera.</p> : null}
              <Lines lines={invoice.currentState.lines} currency={invoice.currency} />
              {advanceAfter != null ? (
                <p className="inv-help">
                  Nabywca: {invoice.currentState.buyer.name ?? '—'}. Wartość zamówienia: {formatMoney(invoice.currentState.totalGross, invoice.currency)}. Zaliczka:{' '}
                  <b data-testid="current-gross">{formatMoney(Math.round(advanceAfter * 100) / 100, invoice.currency)}</b>
                </p>
              ) : (
                <p className="inv-help">
                  Nabywca: {invoice.currentState.buyer.name ?? '—'}. Razem brutto:{' '}
                  <b data-testid="current-gross">{formatMoney(invoice.currentState.totalGross, invoice.currency)}</b>
                </p>
              )}
            </section>
          ) : null}
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

      <ConfirmDialog
        open={wrongNipOpen}
        title="Błędny NIP nabywcy?"
        message="NIP-u nie zmienisz korektą. Utworzymy korektę do zera z obecnym NIP-em. Po jej przyjęciu w KSeF wystawisz poprawną fakturę z właściwym NIP-em."
        confirmLabel="Utwórz korektę do zera"
        pendingLabel="Tworzenie…"
        isPending={correct.isPending}
        onConfirm={() => correct.mutate('WrongBuyerId')}
        onCancel={() => setWrongNipOpen(false)}
      />
    </section>
  )
}

function Lines({ lines, currency }: { lines: IssuedInvoiceLineResponse[]; currency: string }): ReactElement {
  return (
    <table className="lines">
      <thead>
        <tr>
          <th scope="col">Ilość</th>
          <th scope="col">Cena netto</th>
          <th scope="col">Stawka</th>
          <th scope="col">Netto</th>
        </tr>
      </thead>
      {lines.map((line) => (
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
            <td>{formatMoney(line.netAmount, currency)}</td>
          </tr>
        </tbody>
      ))}
    </table>
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
