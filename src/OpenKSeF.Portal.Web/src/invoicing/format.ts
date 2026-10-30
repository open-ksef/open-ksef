const moneyFormat = new Intl.NumberFormat('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

export function formatMoney(amount: number, currency = 'PLN'): string {
  return `${moneyFormat.format(amount)} ${currency}`
}

export function formatDate(isoDate: string | null | undefined): string {
  const match = isoDate ? /^(\d{4})-(\d{2})-(\d{2})/.exec(isoDate) : null
  return match ? `${match[3]}.${match[2]}.${match[1]}` : '—'
}

export function formatNrb(digits: string | null | undefined): string {
  if (!digits) return '—'
  return digits.length === 26 ? `${digits.slice(0, 2)} ${digits.slice(2).replace(/(\d{4})(?=\d)/g, '$1 ')}` : digits
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
