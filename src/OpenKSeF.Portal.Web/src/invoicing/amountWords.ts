const PL_UNITS = ['', 'jeden', 'dwa', 'trzy', 'cztery', 'pięć', 'sześć', 'siedem', 'osiem', 'dziewięć']
const PL_TEENS = ['dziesięć', 'jedenaście', 'dwanaście', 'trzynaście', 'czternaście', 'piętnaście', 'szesnaście', 'siedemnaście', 'osiemnaście', 'dziewiętnaście']
const PL_TENS = ['', '', 'dwadzieścia', 'trzydzieści', 'czterdzieści', 'pięćdziesiąt', 'sześćdziesiąt', 'siedemdziesiąt', 'osiemdziesiąt', 'dziewięćdziesiąt']
const PL_HUNDREDS = ['', 'sto', 'dwieście', 'trzysta', 'czterysta', 'pięćset', 'sześćset', 'siedemset', 'osiemset', 'dziewięćset']
const PL_SCALES: [string, string, string][] = [
  ['tysiąc', 'tysiące', 'tysięcy'],
  ['milion', 'miliony', 'milionów'],
  ['miliard', 'miliardy', 'miliardów'],
]

const EN_UNITS = ['', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen']
const EN_TENS = ['', '', 'twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety']
const EN_SCALES = ['thousand', 'million', 'billion']

const CURRENCY_NAMES: Record<string, { pl: [string, string, string]; en: [string, string] }> = {
  PLN: { pl: ['złoty', 'złote', 'złotych'], en: ['Polish zloty', 'Polish zlotys'] },
  EUR: { pl: ['euro', 'euro', 'euro'], en: ['euro', 'euro'] },
  USD: { pl: ['dolar', 'dolary', 'dolarów'], en: ['US dollar', 'US dollars'] },
  GBP: { pl: ['funt', 'funty', 'funtów'], en: ['British pound', 'British pounds'] },
  CHF: { pl: ['frank', 'franki', 'franków'], en: ['Swiss franc', 'Swiss francs'] },
}

const MAX_WHOLE_DIGITS = 12

function polishForm<T>(value: number, [one, few, many]: [T, T, T]): T {
  if (value === 1) return one
  const lastTwo = value % 100
  const last = value % 10
  return lastTwo >= 12 && lastTwo <= 14 ? many : last >= 2 && last <= 4 ? few : many
}

function polishGroup(value: number): string[] {
  const rest = value % 100
  const tensAndUnits = rest >= 10 && rest < 20 ? [PL_TEENS[rest - 10]] : [PL_TENS[Math.floor(rest / 10)], PL_UNITS[rest % 10]]
  return [PL_HUNDREDS[Math.floor(value / 100)], ...tensAndUnits].filter(Boolean)
}

function englishGroup(value: number): string {
  const rest = value % 100
  const tens = rest < 20 ? EN_UNITS[rest] : [EN_TENS[Math.floor(rest / 10)], EN_UNITS[rest % 10]].filter(Boolean).join('-')
  const hundreds = value >= 100 ? `${EN_UNITS[Math.floor(value / 100)]} hundred` : ''
  return hundreds && tens ? `${hundreds} and ${tens}` : hundreds || tens
}

function groupsOf(whole: number): number[] {
  const groups: number[] = []
  for (let rest = whole; rest > 0; rest = Math.floor(rest / 1000)) groups.push(rest % 1000)
  return groups
}

function polishNumber(whole: number): string {
  if (whole === 0) return 'zero'
  const words: string[] = []
  groupsOf(whole).forEach((group, scale) => {
    if (group === 0) return
    const parts = scale === 0 ? polishGroup(group) : group === 1 ? [PL_SCALES[scale - 1][0]] : [...polishGroup(group), polishForm(group, PL_SCALES[scale - 1])]
    words.unshift(...parts)
  })
  return words.join(' ')
}

function englishNumber(whole: number): string {
  if (whole === 0) return 'zero'
  const groups = groupsOf(whole)
  const words: string[] = []
  for (let scale = groups.length - 1; scale >= 0; scale--) {
    const group = groups[scale]
    if (group === 0) continue
    const text = englishGroup(group)
    const joined = scale === 0 && group < 100 && words.length > 0 ? `and ${text}` : text
    words.push(scale === 0 ? joined : `${joined} ${EN_SCALES[scale - 1]}`)
  }
  return words.join(' ')
}

export function amountInWords(amount: string, currency: string): { pl: string; en: string } | null {
  const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(amount)
  if (!match) return null
  const digits = match[1].replace(/^0+(?=\d)/, '')
  if (digits.length > MAX_WHOLE_DIGITS) return null
  const whole = Number(digits)
  const minor = `${(match[2] ?? '').padEnd(2, '0')}/100`
  const names = CURRENCY_NAMES[currency]
  const polishName = names ? polishForm(whole, names.pl) : currency
  const englishName = names ? (whole === 1 ? names.en[0] : names.en[1]) : currency
  return {
    pl: `${polishNumber(whole)} ${polishName} ${minor}`,
    en: `${englishNumber(whole)} ${englishName} ${minor}`,
  }
}
