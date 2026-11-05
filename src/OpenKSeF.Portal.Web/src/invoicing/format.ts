import { isValidNrb } from './nrb'

const moneyFormat = new Intl.NumberFormat('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })

export function formatAmount(amount: number): string {
  return moneyFormat.format(amount)
}

export function formatMoney(amount: number, currency: string): string {
  return `${formatAmount(amount)} ${currency}`
}

export function formatDecimal(value: number, minimumFractionDigits: number): string {
  return value.toLocaleString('pl-PL', { minimumFractionDigits, maximumFractionDigits: 6, useGrouping: 'always' })
}

export function formatDate(isoDate: string | null | undefined): string {
  const match = isoDate ? /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate) : null
  return match ? `${match[3]}.${match[2]}.${match[1]}` : '—'
}

export function formatNrb(digits: string | null | undefined): string {
  if (!digits) return '—'
  const nrb = digits.replace(/[\s-]/g, '').replace(/^PL/i, '')
  return /^\d{26}$/.test(nrb) ? `${nrb.slice(0, 2)} ${nrb.slice(2).replace(/(\d{4})(?=\d)/g, '$1 ')}` : digits
}

export const formatIban = (iban: string): string => iban.replace(/(.{4})(?=.)/g, '$1 ')

export function accountDigits(account: string | null | undefined): string | null {
  const nrb = account?.replace(/[\s-]/g, '').replace(/^PL/i, '') ?? ''
  return isValidNrb(nrb) ? nrb : null
}

export function todayIso(now = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

export function addDaysIso(isoDate: string, days: number): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  const date = new Date(Date.UTC(year, month - 1, day + days))
  return date.toISOString().slice(0, 10)
}

const dateTimeFormat = new Intl.DateTimeFormat('pl-PL', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

const warsawDateFormat = new Intl.DateTimeFormat('pl-PL', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  timeZone: 'Europe/Warsaw',
})

export function formatWarsawDate(iso: string | null | undefined): string {
  return iso ? warsawDateFormat.format(new Date(iso)) : '—'
}

export function formatDateTime(iso: string | null | undefined): string {
  return iso ? dateTimeFormat.format(new Date(iso)) : '—'
}
