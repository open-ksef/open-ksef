import { amountInWords } from './amountWords'
import { countryName } from './foreign'

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
} satisfies Record<string, Bilingual>

const PAYMENT_METHODS: Record<string, Bilingual> = {
  '6': { pl: 'Przelew', en: 'Bank transfer' },
  '1': { pl: 'Gotówka', en: 'Cash' },
  '2': { pl: 'Karta', en: 'Card' },
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
  const lines = [...(fa?.children ?? [])].filter((element) => element.localName === 'FaWiersz')

  const currency = text(fa, 'KodWaluty') ?? ''
  const issueDate = text(fa, 'P_1') ?? ''
  const number = text(fa, 'P_2') ?? ''
  const total = text(fa, 'P_15') ?? '0.00'
  const paid = text(payment, 'Zaplacono') === '1'
  const money = (digits: string): string => `${formatPrintNumber(digits, main)} ${currency}`

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
    text(line, 'NrWierszaFa') ?? '',
    itemName(text(line, 'P_7') ?? '', main),
    { text: formatPrintNumber(text(line, 'P_8B') ?? '', main), alignment: 'right' },
    unitCell(text(line, 'P_8A') ?? '', main),
    { text: formatPrintNumber(text(line, 'P_9A') ?? '', main), alignment: 'right' },
    { text: formatPrintNumber(text(line, 'P_11') ?? '', main), alignment: 'right' },
    text(line, 'P_12') ?? '',
  ])
  const hasNp = lines.some((line) => text(line, 'P_12')?.startsWith('np'))
  const rate = text(lines[0], 'KursWaluty')
  const words = amountInWords(total, currency)
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
              { text: main === 'en' ? `Invoice no. ${number}` : `Faktura nr ${number}`, fontSize: TITLE_SIZE, bold: true },
              { text: main === 'en' ? `Faktura nr ${number}` : `Invoice no. ${number}`, fontSize: MAIN_SIZE, color: MUTED },
            ],
          },
          {
            width: 210,
            ...(details(
              [row(LABELS.issueDate, issueDate, main), row(LABELS.saleDate, text(fa, 'P_6') ?? issueDate, main)],
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
      { ...(stacked(LABELS.items, main) as object), margin: [0, 0, 0, 4] },
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
      {
        columns: [
          { stack: notes.map((note) => (typeof note === 'string' ? note : { ...note, margin: [0, 0, 0, 5] })), width: '*' },
          {
            width: 210,
            ...(details(
              [
                row(LABELS.total, { text: money(total), bold: true, alignment: 'right' }, main),
                row(LABELS.paid, { text: money(paid ? total : '0.00'), alignment: 'right' }, main),
                row(LABELS.balance, { text: money(paid ? '0.00' : total), bold: true, alignment: 'right' }, main),
              ],
              100,
            ) as object),
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
