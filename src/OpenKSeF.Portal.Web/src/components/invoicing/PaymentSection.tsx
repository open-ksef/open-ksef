import type { ReactElement } from 'react'

import type { PaymentMethod } from '@/api/types'
import { addDaysIso, formatNrb } from '@/invoicing/format'

interface PaymentSectionProps {
  paymentMethod: PaymentMethod
  dueDate: string
  issueDate: string
  splitPayment: boolean
  bankAccount: string | null
  totalGross: number
  onChange: (value: { paymentMethod: PaymentMethod; dueDate: string; splitPayment: boolean }) => void
  fieldErrors?: Record<string, string>
}

const methods: { method: PaymentMethod; label: string }[] = [
  { method: 'Transfer', label: 'Przelew' },
  { method: 'Cash', label: 'Gotówka' },
  { method: 'Card', label: 'Karta' },
]

const MPP_THRESHOLD = 15000

export function PaymentSection({
  paymentMethod,
  dueDate,
  issueDate,
  splitPayment,
  bankAccount,
  totalGross,
  onChange,
  fieldErrors = {},
}: PaymentSectionProps): ReactElement {
  const current = { paymentMethod, dueDate, splitPayment }
  const dueError = fieldErrors.dueDate

  return (
    <section className="inv-card" aria-labelledby="payment-title">
      <h2 id="payment-title">Płatność</h2>
      <div className="inv-segmented" role="group" aria-label="Forma płatności">
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
      <div className="inv-grid">
        <div className="ui-form-group">
          <label htmlFor="due-date">Termin płatności</label>
          <input
            id="due-date"
            type="date"
            value={dueDate}
            aria-invalid={dueError ? 'true' : 'false'}
            aria-describedby={dueError ? 'due-date-error' : undefined}
            onChange={(event) => onChange({ ...current, dueDate: event.target.value })}
          />
          {dueError ? (
            <span className="inv-field-error" id="due-date-error">
              {dueError}
            </span>
          ) : null}
          <div className="inv-chips" role="group" aria-label="Termin płatności od daty wystawienia">
            {[7, 14, 30].map((days) => {
              const target = issueDate ? addDaysIso(issueDate, days) : ''
              return (
                <button
                  key={days}
                  type="button"
                  disabled={!issueDate}
                  aria-pressed={target !== '' && dueDate === target}
                  onClick={() => onChange({ ...current, dueDate: target })}
                >
                  {days} dni
                </button>
              )
            })}
          </div>
        </div>
        {paymentMethod === 'Transfer' ? (
          <div className="ui-form-group">
            <span id="bank-account-label">Rachunek</span>
            <span className="mono" aria-labelledby="bank-account-label">
              {formatNrb(bankAccount)}
            </span>
          </div>
        ) : null}
      </div>
      <label className="inv-check">
        <input
          type="checkbox"
          checked={splitPayment}
          onChange={(event) => onChange({ ...current, splitPayment: event.target.checked })}
        />
        Mechanizm podzielonej płatności
      </label>
      {totalGross > MPP_THRESHOLD && !splitPayment ? (
        <div className="inv-alert inv-alert--warning" role="status">
          Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15).
        </div>
      ) : null}
    </section>
  )
}
