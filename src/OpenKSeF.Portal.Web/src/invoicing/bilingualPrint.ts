import type { VatRateCode } from '@/api/types'
import { amountInWords } from './amountWords'
import { countryName } from './foreign'
import { computeTotals } from './totals'

export type PrintVariant = 'EN-PL' | 'PL-EN'

type Language = 'pl' | 'en'
type Bilingual = Record<Language, string>
type DocNode = Record<string, unknown> | string

const LABELS = {
  issueDate: { pl: 'Data wystawienia', en: 'Issue date' },
  saleDate: { pl: 'Data sprzedaży', en: 'Date of sale' },
  seller: { pl: 'Sprzedawca', en: 'Seller' },
  buyer: { pl: 'Nabywca', en: 'Buyer' },
  sellerTaxId: { pl: 'NIP', en: 'Tax ID (NIP)' },
  buyerVatUe: { pl: 'Numer VAT UE', en: 'EU VAT number' },
  buyerTaxNumber: { pl: 'Numer podatkowy', en: 'Tax number' },
  items: { pl: 'Pozycje', en: 'Items' },
  lineNo: { pl: 'Lp.', en: 'No.' },
  item: { pl: 'Nazwa towaru lub usługi', en: 'Description' },
  quantity: { pl: 'Ilość', en: 'Quantity' },
  unit: { pl: 'Jm.', en: 'Unit' },
  price: { pl: 'Cena jedn. netto', en: 'Unit net price' },
  net: { pl: 'Wartość netto', en: 'Net amount' },
  vatRate: { pl: 'Stawka VAT', en: 'VAT rate' },
  rateLegend: { pl: 'np: niepodlegające opodatkowaniu w Polsce', en: 'np: not subject to VAT in Poland' },
  total: { pl: 'Razem', en: 'Total' },
  paid: { pl: 'Zapłacono', en: 'Paid' },
  balance: { pl: 'Pozostało do zapłaty', en: 'Balance due' },
  inWords: { pl: 'Słownie', en: 'In words' },
  reverseCharge: { pl: 'Odwrotne obciążenie', en: 'Reverse charge' },
  cashAccounting: { pl: 'Metoda kasowa', en: 'Cash accounting' },
  paymentMethod: { pl: 'Sposób zapłaty', en: 'Payment method' },
  dueDate: { pl: 'Termin zapłaty', en: 'Due date' },
  bank: { pl: 'Bank', en: 'Bank' },
  iban: { pl: 'Numer rachunku (IBAN)', en: 'IBAN' },
  swift: { pl: 'SWIFT', en: 'SWIFT' },
  ksefNumber: { pl: 'Numer KSeF', en: 'KSeF number' },
  kodHeading: { pl: 'Kod weryfikacyjny KSeF (KOD I)', en: 'KSeF verification code (QR)' },
  kodHint: { pl: 'Zeskanuj, aby sprawdzić fakturę w KSeF', en: 'Scan to verify this invoice in KSeF' },
  correctedInvoice: { pl: 'Faktura korygowana', en: 'Corrected invoice' },
  correctedIssueDate: { pl: 'Data wystawienia faktury korygowanej', en: 'Issue date of the corrected invoice' },
  correctedKsefNumber: { pl: 'Numer KSeF faktury korygowanej', en: 'KSeF number of the corrected invoice' },
  correctionReason: { pl: 'Przyczyna korekty', en: 'Reason for correction' },
  before: { pl: 'przed', en: 'before' },
  after: { pl: 'po', en: 'after' },
  differences: { pl: 'Różnice w stawkach', en: 'Differences by rate' },
  vat: { pl: 'Kwota VAT', en: 'VAT amount' },
  totalDifference: { pl: 'Różnica razem', en: 'Total difference' },
  refund: { pl: 'Do zwrotu', en: 'To be refunded' },
  order: { pl: 'Zamówienie', en: 'Order' },
  orderValue: { pl: 'Wartość zamówienia', en: 'Order value' },
  advanceByRate: { pl: 'Zaliczka w stawkach', en: 'Advance by rate' },
  advanceReceived: { pl: 'Otrzymana zaliczka', en: 'Advance received' },
  paymentDate: { pl: 'Data otrzymania zapłaty', en: 'Payment received on' },
  advanceInvoices: { pl: 'Faktury zaliczkowe', en: 'Advance invoices' },
  balanceByRate: { pl: 'Do zapłaty w stawkach po odjęciu zaliczek', en: 'Balance by rate after advances' },
  grossTotal: { pl: 'Wartość brutto', en: 'Gross total' },
  advancesDeducted: { pl: 'Zaliczki odliczone', en: 'Advances deducted' },
  amountBefore: { pl: 'Kwota przed korektą', en: 'Amount before correction' },
} satisfies Record<string, Bilingual>

const PAYMENT_METHODS: Record<string, Bilingual> = {
  '6': { pl: 'Przelew', en: 'Bank transfer' },
  '1': { pl: 'Gotówka', en: 'Cash' },
  '2': { pl: 'Karta', en: 'Card' },
}

const BUCKETS: [string, string][] = [
  ['1', '23%'],
  ['2', '8%'],
  ['3', '5%'],
  ['4', '4%'],
  ['5', 'OSS'],
  ['6_1', '0% (kraj)'],
  ['6_2', '0% WDT'],
  ['6_3', '0% EX'],
  ['7', 'zw'],
  ['8', 'np I'],
  ['9', 'np II'],
  ['10', 'oo'],
  ['11', 'marża'],
]

const TITLES: Record<string, Bilingual> = {
  VAT: { pl: 'Faktura', en: 'Invoice' },
  KOR: { pl: 'Faktura korygująca', en: 'Corrective invoice' },
  ZAL: { pl: 'Faktura zaliczkowa', en: 'Advance invoice' },
  ROZ: { pl: 'Faktura rozliczająca', en: 'Final invoice' },
  KOR_ZAL: { pl: 'Korekta faktury zaliczkowej', en: 'Corrective advance invoice' },
  KOR_ROZ: { pl: 'Korekta faktury rozliczającej', en: 'Corrective final invoice' },
}

const ORDER_FIELDS: Record<string, string> = {
  NrWierszaFa: 'NrWierszaZam',
  P_7: 'P_7Z',
  P_8A: 'P_8AZ',
  P_8B: 'P_8BZ',
  P_9A: 'P_9AZ',
  P_11: 'P_11NettoZ',
  P_12: 'P_12Z',
  StanPrzed: 'StanPrzedZ',
}

const UNITS: Record<string, string> = { 'szt.': 'pcs', 'godz.': 'hours', 'usł.': 'service' }

const MAIN_SIZE = 9
const SMALL_SIZE = 7
const TITLE_SIZE = 16
const QR_QUIET_ZONE = 14
const MUTED = '#5b6474'

export function splitItemName(name: string): Bilingual | null {
  let cut = name.indexOf(' / ')
  let separator = 3
  if (cut < 0) {
    cut = name.indexOf('/')
    separator = 1
    if (cut < 1 || !/\p{Ll}/u.test(name[cut - 1]) || !/\p{Lu}/u.test(name[cut + 1] ?? '')) return null
  }
  const pl = name.slice(0, cut).trim()
  const en = name.slice(cut + separator).trim()
  return pl && en ? { pl, en } : null
}

export function formatPrintNumber(digits: string, language: Language): string {
  const [whole, fraction] = digits.split('.')
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, language === 'pl' ? '\u00a0' : ',')
  return fraction === undefined ? grouped : `${grouped}${language === 'pl' ? ',' : '.'}${fraction}`
}

const child = (parent: Element | null | undefined, name: string): Element | null =>
  parent ? ([...parent.children].find((element) => element.localName === name) ?? null) : null

const text = (parent: Element | null | undefined, name: string): string | null => child(parent, name)?.textContent?.trim() || null

const stacked = (value: Bilingual, main: Language, extra: Record<string, unknown> = {}): DocNode => {
  const other = main === 'en' ? 'pl' : 'en'
  return {
    stack: value[main] === value[other]
      ? [{ text: value[main], fontSize: MAIN_SIZE }]
      : [
          { text: value[main], fontSize: MAIN_SIZE },
          { text: value[other], fontSize: SMALL_SIZE, color: MUTED },
        ],
    ...extra,
  }
}

const row = (label: Bilingual, value: DocNode, main: Language): DocNode[] => [stacked(label, main), value]

const plainRows = { hLineWidth: () => 0, vLineWidth: () => 0, paddingLeft: () => 0, paddingTop: () => 2, paddingBottom: () => 2 }

const details = (rows: DocNode[][], labelWidth: number): DocNode | null =>
  rows.length ? { table: { widths: [labelWidth, '*'], body: rows }, layout: plainRows } : null

const countryLine = (code: string | null, main: Language): DocNode | null =>
  code ? stacked({ pl: countryName(code, 'pl'), en: countryName(code, 'en') }, main) : null

function party(label: Bilingual, element: Element | null, main: Language, idRows: DocNode[][]): DocNode {
  const address = child(element, 'Adres')
  return {
    stack: [
      { ...(stacked(label, main) as object), margin: [0, 0, 0, 4] },
      { text: text(child(element, 'DaneIdentyfikacyjne'), 'Nazwa') ?? '', bold: true },
      text(address, 'AdresL1'),
      text(address, 'AdresL2'),
      countryLine(text(address, 'KodKraju'), main),
      details(idRows, 90),
    ].filter(Boolean),
  }
}

function itemName(name: string, main: Language): DocNode {
  const split = splitItemName(name)
  return split ? stacked(split, main) : { text: name, fontSize: MAIN_SIZE }
}

function unitCell(unit: string, main: Language): DocNode {
  return UNITS[unit] ? stacked({ pl: unit, en: UNITS[unit] }, main) : { text: unit, fontSize: MAIN_SIZE }
}

export function buildPrintDocument(xml: string, ksefNumber: string, verificationUrl: string, variant: PrintVariant): Record<string, unknown> {
  const parsed = new DOMParser().parseFromString(xml, 'application/xml')
  const root = parsed.documentElement
  if (parsed.getElementsByTagName('parsererror').length > 0 || root.localName !== 'Faktura') {
    throw new Error('Nie udało się odczytać XML faktury')
  }

  const main: Language = variant === 'EN-PL' ? 'en' : 'pl'
  const seller = child(root, 'Podmiot1')
  const buyer = child(root, 'Podmiot2')
  const buyerId = child(buyer, 'DaneIdentyfikacyjne')
  const fa = child(root, 'Fa')
  const annotations = child(fa, 'Adnotacje')
  const payment = child(fa, 'Platnosc')
  const account = child(payment, 'RachunekBankowy')
  const kind = text(fa, 'RodzajFaktury') ?? 'VAT'
  const order = child(fa, 'Zamowienie')
  const fromOrder = Boolean(order) && ![...(fa?.children ?? [])].some((element) => element.localName === 'FaWiersz')
  const lines = [...((fromOrder ? order : fa)?.children ?? [])].filter((element) => element.localName === (fromOrder ? 'ZamowienieWiersz' : 'FaWiersz'))
  const cell = (line: Element | undefined, name: string): string | null => text(line, fromOrder ? ORDER_FIELDS[name] : name)
  const advanceNumbers = [...(fa?.getElementsByTagNameNS('*', 'NrKSeFFaZaliczkowej') ?? [])].map((element) => element.textContent?.trim() ?? '')

  const currency = text(fa, 'KodWaluty') ?? ''
  const issueDate = text(fa, 'P_1') ?? ''
  const number = text(fa, 'P_2') ?? ''
  const total = text(fa, 'P_15') ?? '0.00'
  const paid = text(payment, 'Zaplacono') === '1'
  const corrective = kind.startsWith('KOR')
  const isAdvance = kind === 'ZAL'
  const isFinal = kind === 'ROZ'
  const corrected = child(fa, 'DaneFaKorygowanej')
  const refund = total.startsWith('-')
  const amount = total.replace('-', '')
  const money = (digits: string): string =>
    digits.startsWith('-') ? `-${formatPrintNumber(digits.slice(1), main)} ${currency}` : `${formatPrintNumber(digits, main)} ${currency}`
  const heading = TITLES[kind] ?? TITLES.VAT
  const title: Bilingual = { pl: `${heading.pl} nr ${number}`, en: `${heading.en} no. ${number}` }

  const sellerIdValue = `${text(seller, 'PrefiksPodatnika') ?? ''}${text(child(seller, 'DaneIdentyfikacyjne'), 'NIP') ?? ''}`
  const sellerIdRows = sellerIdValue ? [row(LABELS.sellerTaxId, sellerIdValue, main)] : []
  const vatUe = text(buyerId, 'NrVatUE')
  const taxNumber = text(buyerId, 'NrID')
  const buyerIdRows = vatUe
    ? [row(LABELS.buyerVatUe, `${text(buyerId, 'KodUE') ?? ''}${vatUe}`, main)]
    : taxNumber
      ? [row(LABELS.buyerTaxNumber, `${text(buyerId, 'KodKraju') ?? ''} ${taxNumber}`.trim(), main)]
      : []

  const itemRows: DocNode[][] = lines.map((line) => [
    corrective
      ? { stack: [cell(line, 'NrWierszaFa') ?? '', stacked(cell(line, 'StanPrzed') === '1' ? LABELS.before : LABELS.after, main)] }
      : (cell(line, 'NrWierszaFa') ?? ''),
    itemName(cell(line, 'P_7') ?? '', main),
    { text: formatPrintNumber(cell(line, 'P_8B') ?? '', main), alignment: 'right' },
    unitCell(cell(line, 'P_8A') ?? '', main),
    { text: formatPrintNumber(cell(line, 'P_9A') ?? '', main), alignment: 'right' },
    { text: formatPrintNumber(cell(line, 'P_11') ?? '', main), alignment: 'right' },
    cell(line, 'P_12') ?? '',
  ])
  const hasNp = lines.some((line) => cell(line, 'P_12')?.startsWith('np'))
  const rate = text(lines[0], 'KursWaluty') ?? text(fa, 'KursWalutyZ')
  const words = amountInWords(amount, currency)
  const bucketTitle = corrective ? LABELS.differences : isAdvance ? LABELS.advanceByRate : LABELS.balanceByRate
  const differenceRows: DocNode[][] = corrective || isAdvance || isFinal
    ? BUCKETS.filter(([bucket]) => text(fa, `P_13_${bucket}`) !== null).map(([bucket, label]) => [
        label,
        { text: money(text(fa, `P_13_${bucket}`) ?? ''), alignment: 'right' },
        { text: text(fa, `P_14_${bucket}`) ? money(text(fa, `P_14_${bucket}`) ?? '') : '', alignment: 'right' },
      ])
    : []
  const correctionRows: DocNode[][] = corrective
    ? [
        row(LABELS.correctedInvoice, { text: text(corrected, 'NrFaKorygowanej') ?? '', bold: true }, main),
        row(LABELS.correctedIssueDate, text(corrected, 'DataWystFaKorygowanej') ?? '', main),
        row(LABELS.correctedKsefNumber, text(corrected, 'NrKSeFFaKorygowanej') ?? '', main),
        row(LABELS.correctionReason, text(fa, 'PrzyczynaKorekty') ?? '', main),
        ...(text(fa, 'P_15ZK') ? [row(LABELS.amountBefore, money(text(fa, 'P_15ZK') ?? ''), main)] : []),
      ]
    : advanceNumbers.length
      ? [row(LABELS.advanceInvoices, { stack: advanceNumbers }, main)]
      : []
  const grossTotal = isFinal
    ? computeTotals(
        lines.map((line) => ({
          quantity: Number(cell(line, 'P_8B') ?? 0),
          unitNetPrice: Number(cell(line, 'P_9A') ?? 0),
          vatRate: (cell(line, 'P_12') ?? '') as VatRateCode,
        })),
      ).totalGross.toFixed(2)
    : '0.00'
  const totalRows: DocNode[][] = corrective
    ? [
        row(LABELS.totalDifference, { text: money(total), bold: true, alignment: 'right' }, main),
        row(refund ? LABELS.refund : LABELS.balance, { text: money(amount), bold: true, alignment: 'right' }, main),
      ]
    : isAdvance
      ? [
          row(LABELS.orderValue, { text: money(text(order, 'WartoscZamowienia') ?? ''), alignment: 'right' }, main),
          row(LABELS.advanceReceived, { text: money(total), bold: true, alignment: 'right' }, main),
          row(LABELS.paymentDate, { text: text(payment, 'DataZaplaty') ?? '', alignment: 'right' }, main),
        ]
      : isFinal
        ? [
            row(LABELS.grossTotal, { text: money(grossTotal), alignment: 'right' }, main),
            row(LABELS.advancesDeducted, { text: money((Number(grossTotal) - Number(total)).toFixed(2)), alignment: 'right' }, main),
            row(LABELS.balance, { text: money(total), bold: true, alignment: 'right' }, main),
          ]
        : [
        row(LABELS.total, { text: money(total), bold: true, alignment: 'right' }, main),
        row(LABELS.paid, { text: money(paid ? total : '0.00'), alignment: 'right' }, main),
        row(LABELS.balance, { text: money(paid ? '0.00' : total), bold: true, alignment: 'right' }, main),
      ]
  const method = PAYMENT_METHODS[text(payment, 'FormaPlatnosci') ?? '']
  const dueDate = text(child(payment, 'TerminPlatnosci'), 'Termin')
  const bank = text(account, 'NazwaBanku')
  const iban = text(account, 'NrRB')
  const swift = text(account, 'SWIFT')

  const notes: DocNode[] = [
    words ? { columns: [{ width: 70, ...(stacked(LABELS.inWords, main) as object) }, stacked(words, main)] } : null,
    rate
      ? stacked(
          {
            pl: `Kurs waluty: 1 ${currency} = ${formatPrintNumber(rate, main)} PLN`,
            en: `Exchange rate: 1 ${currency} = ${formatPrintNumber(rate, main)} PLN`,
          },
          main,
        )
      : null,
    text(annotations, 'P_18') === '1' ? stacked(LABELS.reverseCharge, main) : null,
    text(annotations, 'P_16') === '1' ? stacked(LABELS.cashAccounting, main) : null,
  ].filter((node): node is DocNode => node !== null)

  const paymentRows: DocNode[][] = [
    method ? row(LABELS.paymentMethod, stacked(method, main), main) : null,
    dueDate ? row(LABELS.dueDate, dueDate, main) : null,
    bank ? row(LABELS.bank, bank, main) : null,
    iban ? row(LABELS.iban, iban, main) : null,
    swift ? row(LABELS.swift, swift, main) : null,
  ].filter((node): node is DocNode[] => node !== null)

  return {
    pageSize: 'A4',
    pageMargins: [36, 36, 36, 36],
    info: { title: number },
    defaultStyle: { font: 'Roboto', fontSize: MAIN_SIZE },
    content: [
      {
        columns: [
          {
            stack: [
              { text: title[main], fontSize: TITLE_SIZE, bold: true },
              { text: title[main === 'en' ? 'pl' : 'en'], fontSize: MAIN_SIZE, color: MUTED },
            ],
          },
          {
            width: 210,
            ...(details(
              [row(LABELS.issueDate, issueDate, main), ...(corrective || isAdvance ? [] : [row(LABELS.saleDate, text(fa, 'P_6') ?? issueDate, main)])],
              110,
            ) as object),
          },
        ],
        margin: [0, 0, 0, 14],
      },
      {
        columns: [party(LABELS.seller, seller, main, sellerIdRows), party(LABELS.buyer, buyer, main, buyerIdRows)],
        columnGap: 18,
        margin: [0, 0, 0, 14],
      },
      correctionRows.length ? { ...(details(correctionRows, 150) as object), margin: [0, 0, 0, 14] } : null,
      { ...(stacked(fromOrder ? LABELS.order : LABELS.items, main) as object), margin: [0, 0, 0, 4] },
      {
        table: {
          headerRows: 1,
          dontBreakRows: true,
          widths: [24, '*', 48, 40, 62, 62, 42],
          body: [
            [LABELS.lineNo, LABELS.item, LABELS.quantity, LABELS.unit, LABELS.price, LABELS.net, LABELS.vatRate].map((label) => stacked(label, main)),
            ...itemRows,
          ],
        },
        layout: 'lightHorizontalLines',
      },
      hasNp ? { ...(stacked(LABELS.rateLegend, main) as object), margin: [0, 4, 0, 0] } : null,
      differenceRows.length
        ? {
            table: {
              headerRows: 1,
              widths: ['*', 90, 90],
              body: [[bucketTitle, LABELS.net, LABELS.vat].map((label) => stacked(label, main)), ...differenceRows],
            },
            layout: 'lightHorizontalLines',
            margin: [0, 12, 0, 0],
          }
        : null,
      {
        columns: [
          { stack: notes.map((note) => (typeof note === 'string' ? note : { ...note, margin: [0, 0, 0, 5] })), width: '*' },
          {
            width: 210,
            ...(details(totalRows, 100) as object),
          },
        ],
        columnGap: 18,
        margin: [0, 12, 0, 14],
      },
      {
        columns: [
          { stack: [details(paymentRows, 110)].filter(Boolean), width: '*' },
          {
            width: 180,
            unbreakable: true,
            stack: [
              stacked(LABELS.kodHeading, main),
              { qr: verificationUrl, fit: 130, eccLevel: 'M', margin: [QR_QUIET_ZONE, QR_QUIET_ZONE, QR_QUIET_ZONE, QR_QUIET_ZONE] },
              { text: ksefNumber, bold: true, fontSize: 8 },
              stacked(LABELS.ksefNumber, main),
              { ...(stacked(LABELS.kodHint, main) as object), margin: [0, 4, 0, 0] },
            ],
          },
        ],
        columnGap: 18,
      },
    ].filter(Boolean),
  }
}

export async function renderBilingualPdf(xml: string, ksefNumber: string, verificationUrl: string, variant: PrintVariant): Promise<Blob> {
  const document = buildPrintDocument(xml, ksefNumber, verificationUrl, variant)
  const [{ default: pdfMake }, { default: vfs }] = await Promise.all([import('pdfmake/build/pdfmake'), import('pdfmake/build/vfs_fonts')])
  pdfMake.addVirtualFileSystem(vfs)
  return pdfMake.createPdf(document).getBlob()
}
