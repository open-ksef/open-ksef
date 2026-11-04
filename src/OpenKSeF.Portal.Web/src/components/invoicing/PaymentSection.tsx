import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import type { PaymentMethod } from '@/api/types'
import type { ForeignAccount } from '@/invoicing/foreign'
import { formatIban, formatNrb } from '@/invoicing/format'

interface PaymentSectionProps {
  paymentMethod: PaymentMethod
  splitPayment: boolean
  bankAccount: string | null
  foreignInvoice?: boolean
  foreignAccount?: ForeignAccount | null
  totalGross: number
  profileHref: string
  onChange: (value: { paymentMethod: PaymentMethod; splitPayment: boolean }) => void
}

const methods: { method: PaymentMethod; label: string }[] = [
  { method: 'Transfer', label: 'Przelew' },
  { method: 'Cash', label: 'Gotówka' },
  { method: 'Card', label: 'Karta' },
]

const MPP_THRESHOLD = 15000

export function PaymentSection({ paymentMethod, splitPayment, bankAccount, foreignInvoice = false, foreignAccount = null, totalGross, profileHref, onChange }: PaymentSectionProps): ReactElement {
  const current = { paymentMethod, splitPayment }

  return (
    <>
      <div className="inv-fields inv-fields--center">
        <div className="inv-chips" role="group" aria-label="Forma płatności">
          {methods.map((option) => (
            <button
              key={option.method}
              type="button"
              aria-pressed={paymentMethod === option.method}
              onClick={() => onChange({ ...current, paymentMethod: option.method })}
            >
              {option.label}
            </button>
          ))}
        </div>
        {foreignInvoice ? null : (
          <label className="inv-check">
            <input
              type="checkbox"
              checked={splitPayment}
              onChange={(event) => onChange({ ...current, splitPayment: event.target.checked })}
            />
            Mechanizm podzielonej płatności
          </label>
        )}
      </div>
      {paymentMethod === 'Transfer' ? (
        <div className="inv-account">
          <span id="bank-account-label" className="inv-caption">
            Rachunek
          </span>
          <span className="mono" aria-labelledby="bank-account-label">
            {foreignInvoice ? (foreignAccount ? formatIban(foreignAccount.iban) : '—') : formatNrb(bankAccount)}
          </span>
          {foreignAccount?.swift ? <span className="mono">SWIFT {foreignAccount.swift}</span> : null}
          {foreignAccount?.bankName ? <span>{foreignAccount.bankName}</span> : null}
          <Link to={profileHref}>Zmień w Danych do faktur</Link>
        </div>
      ) : null}
      {!foreignInvoice && totalGross > MPP_THRESHOLD && !splitPayment ? (
        <div className="inv-alert inv-alert--warning" role="status">
          Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15).
        </div>
      ) : null}
    </>
  )
}
