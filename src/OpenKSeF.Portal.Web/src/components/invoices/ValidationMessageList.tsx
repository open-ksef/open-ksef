import type { ReactElement } from 'react'

import { getInvoiceRuleFamily, type InvoiceRuleFamily } from '@/api/schemas/ruleCodes'
import type { ValidationEnvelope } from '@/api/schemas/invoice'

const familyOrder: InvoiceRuleFamily[] = ['structure', 'parties', 'dates', 'vat', 'advances', 'correction', 'ksef', 'state']

const familyLabels: Record<InvoiceRuleFamily, string> = {
  structure: 'Struktura',
  parties: 'Strony',
  dates: 'Daty',
  vat: 'VAT',
  advances: 'Zaliczki',
  correction: 'Korekta',
  ksef: 'KSeF',
  state: 'Stan',
}

interface ValidationMessageListProps {
  stage: ValidationEnvelope['stage']
  messages: ValidationEnvelope['messages']
}

interface GroupedMessageList {
  family: string
  familyOrder: number
  severityOrder: number
  items: ValidationEnvelope['messages']
}

export function ValidationMessageList({ stage, messages }: ValidationMessageListProps): ReactElement | null {
  if (messages.length === 0) {
    return null
  }

  const groupedMessages = groupMessages(messages)
  const hasErrors = messages.some((message) => message.severity === 'Error')

  return (
    <section
      className="validation-message-list"
      role={hasErrors ? 'alert' : 'status'}
      aria-label={`Wynik walidacji ${stage}`}
    >
      {groupedMessages.map((group) => (
        <section className="validation-message-list__group" key={`${group.severityOrder}-${group.family}`}>
          <h2 className="validation-message-list__group-title">{group.family}</h2>
          <ul className="validation-message-list__items">
            {group.items.map((message) => (
              <li
                className={`validation-message-list__item validation-message-list__item--${message.severity.toLowerCase()}`}
                key={`${message.code}-${message.field ?? 'document'}-${message.messagePl}`}
              >
                <div className="validation-message-list__body">
                  <p className="validation-message-list__text">{message.messagePl}</p>
                  {message.field ? <p className="validation-message-list__field">{message.field}</p> : null}
                </div>
                <code className="validation-message-list__code">{message.code}</code>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </section>
  )
}

function groupMessages(messages: ValidationEnvelope['messages']): GroupedMessageList[] {
  const groups = new Map<string, GroupedMessageList>()

  for (const message of messages) {
    const severityOrder = message.severity === 'Error' ? 0 : 1
    const family = getInvoiceRuleFamily(message.code)

    if (!family) {
      warnAboutUnknownRuleCode(message.code)
    }

    const label = family ? familyLabels[family] : 'Inne'
    const order = family ? familyOrder.indexOf(family) : Number.MAX_SAFE_INTEGER
    const key = `${severityOrder}:${label}`

    if (!groups.has(key)) {
      groups.set(key, {
        family: label,
        familyOrder: order,
        severityOrder,
        items: [],
      })
    }

    groups.get(key)?.items.push(message)
  }

  return [...groups.values()].sort((left, right) => {
    if (left.severityOrder !== right.severityOrder) {
      return left.severityOrder - right.severityOrder
    }

    return left.familyOrder - right.familyOrder
  })
}

function warnAboutUnknownRuleCode(code: string): void {
  if (import.meta.env.DEV) {
    console.warn(`Unknown invoice validation rule code received from backend: ${code}`)
  }
}
