import type { IssuedInvoiceBuyer, IssuedInvoiceResponse, IssuedInvoiceSeller } from '@/api/types'
import { amountInWords } from './amountWords'
import { countryName } from './foreign'
import { formatDate, formatDecimal, formatIban, formatMoney, formatNrb } from './format'
import { vatRateLabel } from './totals'

export const PROFORMA_FOOTER = 'Dokument nie jest fakturą VAT w rozumieniu ustawy o VAT i nie stanowi podstawy do odliczenia podatku.'

const PAYMENT_METHODS = { Transfer: 'Przelew', Cash: 'Gotówka', Card: 'Karta' } as const
const MUTED = '#5b6474'

type DocNode = Record<string, unknown> | string

const plainRows = { hLineWidth: () => 0, vLineWidth: () => 0, paddingLeft: () => 0, paddingTop: () => 2, paddingBottom: () => 2 }

const details = (rows: [string, DocNode][], labelWidth: number): DocNode => ({
  table: { widths: [labelWidth, '*'], body: rows.map(([label, value]) => [{ text: label, color: MUTED }, value]) },
  layout: plainRows,
})

function party(label: string, value: IssuedInvoiceSeller | IssuedInvoiceBuyer): DocNode {
  const buyer = value as IssuedInvoiceBuyer
  const taxId = value.nip
    ? `NIP ${value.nip}`
    : buyer.foreignId
      ? `${buyer.idType === 'VatUe' ? 'Numer VAT UE' : 'Numer podatkowy'} ${buyer.foreignId}`
      : null
  return {
    stack: [
      { text: label, color: MUTED, margin: [0, 0, 0, 4] },
      { text: value.name ?? '', bold: true },
      value.addressLine1,
      value.addressLine2,
      buyer.countryCode ? countryName(buyer.countryCode) : null,
      taxId,
    ].filter(Boolean),
  }
}

export function buildProformaDocument(invoice: IssuedInvoiceResponse): Record<string, unknown> {
  const number = invoice.number ?? ''
  const money = (value: number): string => formatMoney(value, invoice.currency)
  const words = amountInWords(invoice.totalGross.toFixed(2), invoice.currency)
  const transfer = invoice.paymentMethod === 'Transfer'
  const account = invoice.seller.iban ? formatIban(invoice.seller.iban) : invoice.seller.bankAccount ? formatNrb(invoice.seller.bankAccount) : null

  const paymentRows: [string, DocNode][] = [
    ['Sposób zapłaty', PAYMENT_METHODS[invoice.paymentMethod]],
    ['Termin zapłaty', formatDate(invoice.dueDate)],
    ...(transfer && invoice.seller.bankName ? [['Bank', invoice.seller.bankName] as [string, DocNode]] : []),
    ...(transfer && account ? [['Numer rachunku', account] as [string, DocNode]] : []),
    ...(transfer && invoice.seller.swift ? [['SWIFT', invoice.seller.swift] as [string, DocNode]] : []),
    ...(transfer ? [['Tytuł przelewu', number] as [string, DocNode]] : []),
  ]

  return {
    pageSize: 'A4',
    pageMargins: [36, 36, 36, 48],
    info: { title: `Faktura proforma ${number}` },
    defaultStyle: { font: 'Roboto', fontSize: 9 },
    footer: { text: PROFORMA_FOOTER, fontSize: 8, color: MUTED, alignment: 'center', margin: [36, 12, 36, 0] },
    content: [
      {
        columns: [
          { text: `Faktura proforma ${number}`, fontSize: 16, bold: true },
          { width: 200, ...(details([['Data wystawienia', formatDate(invoice.issueDate)]], 100) as object) },
        ],
        margin: [0, 0, 0, 14],
      },
      { columns: [party('Sprzedawca', invoice.seller), party('Nabywca', invoice.buyer)], columnGap: 18, margin: [0, 0, 0, 14] },
      {
        table: {
          headerRows: 1,
          dontBreakRows: true,
          widths: [24, '*', 48, 40, 62, 62, 42],
          body: [
            ['Lp.', 'Nazwa towaru lub usługi', 'Ilość', 'Jm.', 'Cena jedn. netto', 'Wartość netto', 'Stawka VAT'].map((label) => ({ text: label, color: MUTED })),
            ...invoice.lines.map((line) => [
              String(line.lineNo),
              line.name,
              { text: formatDecimal(line.quantity, 0), alignment: 'right' },
              line.unit,
              { text: formatDecimal(line.unitNetPrice, 2), alignment: 'right' },
              { text: money(line.netAmount), alignment: 'right' },
              vatRateLabel(line.vatRate),
            ]),
          ],
        },
        layout: 'lightHorizontalLines',
      },
      {
        table: {
          headerRows: 1,
          widths: ['*', 90, 90, 90],
          body: [
            ['Stawka VAT', 'Netto', 'VAT', 'Brutto'].map((label) => ({ text: label, color: MUTED })),
            ...invoice.vatSummary.map((row) => [
              vatRateLabel(row.vatRate),
              { text: money(row.net), alignment: 'right' },
              { text: money(row.vat), alignment: 'right' },
              { text: money(row.net + row.vat), alignment: 'right' },
            ]),
          ],
        },
        layout: 'lightHorizontalLines',
        margin: [0, 12, 0, 0],
      },
      {
        columns: [
          { stack: [words ? `Słownie: ${words.pl}` : null, invoice.exemptionBasis?.text ? `Podstawa zwolnienia: ${invoice.exemptionBasis.text}` : null].filter(Boolean), width: '*' },
          {
            width: 210,
            ...(details(
              [
                ['Razem netto', { text: money(invoice.totalNet), alignment: 'right' }],
                ['Razem VAT', { text: money(invoice.totalVat), alignment: 'right' }],
                ['Do zapłaty', { text: money(invoice.totalGross), bold: true, alignment: 'right' }],
              ],
              100,
            ) as object),
          },
        ],
        columnGap: 18,
        margin: [0, 12, 0, 14],
      },
      details(paymentRows, 110),
    ],
  }
}

export async function renderProformaPdf(invoice: IssuedInvoiceResponse): Promise<Blob> {
  const document = buildProformaDocument(invoice)
  const [{ default: pdfMake }, { default: vfs }] = await Promise.all([import('pdfmake/build/pdfmake'), import('pdfmake/build/vfs_fonts')])
  pdfMake.addVirtualFileSystem(vfs)
  return pdfMake.createPdf(document).getBlob()
}
