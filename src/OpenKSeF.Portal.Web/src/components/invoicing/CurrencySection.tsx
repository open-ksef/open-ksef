import { useEffect, useRef, useState, type ReactElement } from 'react'

import { getExchangeRateProposal } from '@/api/endpoints/issuedInvoices'
import type { ExchangeRateProposalResponse } from '@/api/types'
import { CURRENCIES } from '@/invoicing/foreign'
import { rateToInput, type DraftFormValue } from '@/invoicing/form'
import { formatDate } from '@/invoicing/format'

interface CurrencySectionProps {
  tenantId: string
  currency: string
  exchangeRate: string
  exchangeRateDate: string | null
  issueDate: string
  saleDate: string
  fieldErrors?: Record<string, string>
  onUpdate: (update: (current: DraftFormValue) => DraftFormValue) => void
}

export function CurrencySection({
  tenantId,
  currency,
  exchangeRate,
  exchangeRateDate,
  issueDate,
  saleDate,
  fieldErrors = {},
  onUpdate,
}: CurrencySectionProps): ReactElement {
  const needsRate = currency !== 'PLN'
  const key = needsRate && issueDate && saleDate ? `${currency}|${issueDate}|${saleDate}` : null
  const [loaded, setLoaded] = useState<{ key: string; proposal: ExchangeRateProposalResponse } | null>(null)
  const [attempt, setAttempt] = useState(0)
  const rateOwner = useRef(key)

  useEffect(() => {
    if (!key) return
    let active = true
    getExchangeRateProposal(tenantId, { currency, issueDate, saleDate })
      .catch(() => ({ currency, rate: null, tableDate: null }))
      .then((proposal) => {
        if (!active) return
        setLoaded({ key, proposal })
        const keepRate = rateOwner.current === key
        rateOwner.current = key
        onUpdate((current) => {
          if (current.exchangeRate.trim() !== '' && current.exchangeRateDate === null) return current
          if (proposal.rate !== null) {
            return { ...current, exchangeRate: rateToInput(proposal.rate), exchangeRateDate: proposal.tableDate }
          }
          return keepRate ? current : { ...current, exchangeRate: '', exchangeRateDate: null }
        })
      })
    return () => {
      active = false
    }
  }, [key, attempt, tenantId, currency, issueDate, saleDate, onUpdate])

  const proposal = loaded?.key === key ? loaded.proposal : null
  const rateError = fieldErrors.exchangeRate
  const currencyError = fieldErrors.currency
  const typed = exchangeRate.trim() !== '' && exchangeRateDate === null
  const nbpDiffers = proposal?.rate != null && rateToInput(proposal.rate) !== exchangeRate

  function sourceText(): string {
    const source = exchangeRateDate
      ? `Średni kurs NBP z tabeli z ${formatDate(exchangeRateDate)}.`
      : typed
        ? 'Kurs wpisany ręcznie.'
        : proposal
          ? 'Brak kursu NBP. Wpisz kurs ręcznie.'
          : 'Pobieramy kurs NBP…'
    return nbpDiffers ? `${source} NBP z ${formatDate(proposal?.tableDate)}: ${rateToInput(proposal?.rate)}.` : source
  }

  return (
    <div className="inv-fields">
      <div className="ui-form-group inv-date">
        <label htmlFor="invoice-currency">Waluta</label>
        <select
          id="invoice-currency"
          value={currency}
          aria-invalid={currencyError ? 'true' : 'false'}
          aria-describedby={currencyError ? 'invoice-currency-error' : undefined}
          onChange={(event) => {
            const next = event.target.value
            onUpdate((current) => ({ ...current, currency: next, exchangeRate: '', exchangeRateDate: null }))
          }}
        >
          {(CURRENCIES.includes(currency) ? CURRENCIES : [currency, ...CURRENCIES]).map((code) => (
            <option key={code} value={code}>
              {code}
            </option>
          ))}
        </select>
        {currencyError ? (
          <span className="inv-field-error" id="invoice-currency-error">
            {currencyError}
          </span>
        ) : null}
      </div>
      {needsRate ? (
        <div className="ui-form-group inv-rate">
          <label htmlFor="exchange-rate">
            Kurs: 1 {currency} w PLN<span aria-hidden="true"> *</span>
          </label>
          <input
            id="exchange-rate"
            type="text"
            inputMode="decimal"
            className="num"
            aria-required="true"
            aria-invalid={rateError ? 'true' : 'false'}
            aria-describedby={rateError ? 'exchange-rate-error exchange-rate-source' : 'exchange-rate-source'}
            value={exchangeRate}
            onChange={(event) => {
              const next = event.target.value
              onUpdate((current) => ({ ...current, exchangeRate: next, exchangeRateDate: null }))
            }}
          />
          {rateError ? (
            <span className="inv-field-error" id="exchange-rate-error">
              {rateError}
            </span>
          ) : null}
          <span className="inv-help" id="exchange-rate-source" role="status">
            {sourceText()}
          </span>
          {proposal ? (
            <button
              type="button"
              className="ui-button ui-button--outline ui-button--sm"
              onClick={() => {
                if (nbpDiffers) {
                  onUpdate((current) => ({ ...current, exchangeRate: rateToInput(proposal.rate), exchangeRateDate: proposal.tableDate }))
                } else {
                  setAttempt((value) => value + 1)
                }
              }}
            >
              {nbpDiffers ? 'Użyj kursu NBP' : 'Pobierz kurs ponownie'}
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}
