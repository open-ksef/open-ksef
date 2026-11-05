// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'

import { buildPrintDocument, formatPrintNumber, splitItemName } from './bilingualPrint'

const URL = 'https://qr-test.ksef.mf.gov.pl/invoice/5260250274/03-11-2026/abc'
const KSEF = '5260250274-20261103-ABC-01'

interface InvoiceOptions {
  sellerPrefix?: string
  buyerId?: string
  country?: string
  currency?: string
  rate?: string
  rateCode?: string
  itemName?: string
  unit?: string
  extraAnnotations?: boolean
  paid?: boolean
  bank?: boolean
  amount?: string
}

const xmlFor = ({
  sellerPrefix = '',
  buyerId = '<BrakID>1</BrakID>',
  country = 'GB',
  currency = 'GBP',
  rate = '<KursWaluty>5.1234</KursWaluty>',
  rateCode = 'np I',
  itemName = 'Usługi doradcze/Consulting services',
  unit = 'szt.',
  extraAnnotations = false,
  paid = false,
  bank = true,
  amount = '2350.50',
}: InvoiceOptions = {}): string => `<?xml version="1.0" encoding="UTF-8"?>
<Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
  <Podmiot1>${sellerPrefix}<DaneIdentyfikacyjne><NIP>5260250274</NIP><Nazwa>Sprzedawca Żółty</Nazwa></DaneIdentyfikacyjne>
    <Adres><KodKraju>PL</KodKraju><AdresL1>ul. Prosta 1</AdresL1><AdresL2>00-001 Warszawa</AdresL2></Adres></Podmiot1>
  <Podmiot2><DaneIdentyfikacyjne>${buyerId}<Nazwa>Example Ltd</Nazwa></DaneIdentyfikacyjne>
    <Adres><KodKraju>${country}</KodKraju><AdresL1>1 Sample Street</AdresL1><AdresL2>London</AdresL2></Adres></Podmiot2>
  <Fa><KodWaluty>${currency}</KodWaluty><P_1>2026-11-03</P_1><P_2>FV/7/11/2026</P_2><P_6>2026-10-31</P_6><P_15>${amount}</P_15>
    <Adnotacje><P_16>${extraAnnotations ? 1 : 2}</P_16><P_18>1</P_18></Adnotacje>
    <FaWiersz><NrWierszaFa>1</NrWierszaFa><P_7>${itemName}</P_7><P_8A>${unit}</P_8A><P_8B>1</P_8B><P_9A>1234.50</P_9A><P_11>1234.50</P_11><P_12>${rateCode}</P_12>${rate}</FaWiersz>
    <Platnosc>${paid ? '<Zaplacono>1</Zaplacono>' : ''}<TerminPlatnosci><Termin>2026-11-17</Termin></TerminPlatnosci><FormaPlatnosci>6</FormaPlatnosci>
      ${bank ? '<RachunekBankowy><NrRB>GB82WEST12345698765432</NrRB><SWIFT>TESTGB2L</SWIFT><NazwaBanku>Bank Próbny</NazwaBanku></RachunekBankowy>' : ''}
    </Platnosc></Fa>
</Faktura>`


function flatten(node: unknown, out: { text: string; fontSize?: number }[] = []): { text: string; fontSize?: number }[] {
  if (typeof node === 'string') out.push({ text: node })
  else if (Array.isArray(node)) node.forEach((item) => flatten(item, out))
  else if (node && typeof node === 'object') {
    const record = node as Record<string, unknown>
    if (typeof record.text === 'string') out.push({ text: record.text, fontSize: record.fontSize as number | undefined })
    for (const key of ['stack', 'columns', 'table', 'body', 'content']) flatten(record[key], out)
  }
  return out
}

const texts = (xml: string, variant: 'EN-PL' | 'PL-EN' = 'EN-PL') => flatten(buildPrintDocument(xml, KSEF, URL, variant))
const has = (items: { text: string }[], value: string) => items.some((item) => item.text.replace(/\u00a0/g, ' ') === value)
const sizeOf = (items: { text: string; fontSize?: number }[], value: string) => items.find((item) => item.text === value)?.fontSize

describe('splitItemName', () => {
  it.each([
    ['Usługi doradcze/Consulting services', { pl: 'Usługi doradcze', en: 'Consulting services' }],
    ['Usługi doradcze / Consulting services', { pl: 'Usługi doradcze', en: 'Consulting services' }],
    ['Hosting / domeny / backup', { pl: 'Hosting', en: 'domeny / backup' }],
    ['Wsparcie CI/CD', null],
    ['Konfiguracja TCP/IP', null],
    ['Konsultacje 10/2026', null],
    ['Prędkość km/h', null],
    ['CI/CD Usługi doradcze/Consulting services', null],
    ['Konsultacje / ', null],
    ['/Consulting', null],
    ['bez ukośnika', null],
  ])('REQ-0030 table: %s', (name, expected) => {
    expect(splitItemName(name)).toEqual(expected)
  })
})

describe('formatPrintNumber', () => {
  it('keeps the XML digits and follows the main language', () => {
    expect(formatPrintNumber('1234.50', 'en')).toBe('1,234.50')
    expect(formatPrintNumber('1234.50', 'pl').replace(/\u00a0/g, ' ')).toBe('1 234,50')
    expect(formatPrintNumber('1000000.00', 'en')).toBe('1,000,000.00')
    expect(formatPrintNumber('5.1234', 'pl')).toBe('5,1234')
    expect(formatPrintNumber('10', 'en')).toBe('10')
  })
})

describe('buildPrintDocument', () => {
  it('AC2: English main prints English large and Polish small, title first', () => {
    const items = texts(xmlFor())
    expect(items[0].text).toBe('Invoice no. FV/7/11/2026')
    expect(items[1].text).toBe('Faktura nr FV/7/11/2026')
    expect(sizeOf(items, 'Seller')).toBeGreaterThan(sizeOf(items, 'Sprzedawca') ?? 99)
    expect(sizeOf(items, 'Unit net price')).toBeGreaterThan(sizeOf(items, 'Cena jedn. netto') ?? 99)
  })

  it('AC3: Polish main swaps the sizes and the amount format', () => {
    const items = texts(xmlFor(), 'PL-EN')
    expect(items[0].text).toBe('Faktura nr FV/7/11/2026')
    expect(sizeOf(items, 'Sprzedawca')).toBeGreaterThan(sizeOf(items, 'Seller') ?? 99)
    expect(has(items, '2 350,50 GBP')).toBe(true)
    expect(has(items, '2,350.50 GBP')).toBe(false)
  })

  it('AC4: prints every required value from the XML', () => {
    const items = texts(xmlFor())
    for (const value of [
      'Sprzedawca Żółty',
      'ul. Prosta 1',
      '00-001 Warszawa',
      'Example Ltd',
      '1 Sample Street',
      'London',
      'Consulting services',
      'Usługi doradcze',
      '1,234.50',
      'pcs',
      'szt.',
      'np I',
      '2,350.50 GBP',
      '0.00 GBP',
      'two thousand three hundred and fifty British pounds 50/100',
      'dwa tysiące trzysta pięćdziesiąt funtów 50/100',
      'Exchange rate: 1 GBP = 5.1234 PLN',
      'Kurs waluty: 1 GBP = 5.1234 PLN',
      'Reverse charge',
      'Odwrotne obciążenie',
      '2026-11-03',
      '2026-10-31',
      'Bank transfer',
      'Przelew',
      '2026-11-17',
      'Bank Próbny',
      'GB82WEST12345698765432',
      'TESTGB2L',
      KSEF,
      'np: not subject to VAT in Poland',
    ]) {
      expect(has(items, value), value).toBe(true)
    }
  })

  it('AC5: BrakID prints no tax number, EU prints KodUE + NrVatUE and PL + NIP', () => {
    expect(has(texts(xmlFor()), 'Tax number')).toBe(false)
    expect(has(texts(xmlFor()), 'EU VAT number')).toBe(false)
    expect(has(texts(xmlFor()), '5260250274')).toBe(true)

    const eu = texts(
      xmlFor({
        sellerPrefix: '<PrefiksPodatnika>PL</PrefiksPodatnika>',
        buyerId: '<KodUE>DE</KodUE><NrVatUE>123456789</NrVatUE>',
        country: 'DE',
        currency: 'EUR',
        rateCode: 'np II',
      }),
    )
    expect(has(eu, 'EU VAT number')).toBe(true)
    expect(has(eu, 'DE123456789')).toBe(true)
    expect(has(eu, 'PL5260250274')).toBe(true)

    const third = texts(xmlFor({ buyerId: '<KodKraju>GB</KodKraju><NrID>GB-123</NrID>' }))
    expect(has(third, 'Tax number')).toBe(true)
    expect(has(third, 'GB GB-123')).toBe(true)
  })

  it('AC6: KOD I holds the URL, with the KSeF number directly under it', () => {
    const json = JSON.stringify(buildPrintDocument(xmlFor(), KSEF, URL, 'EN-PL'))
    expect(json).toContain(`"qr":"${URL}"`)
    expect(json.indexOf(`"qr":"${URL}"`)).toBeLessThan(json.indexOf(KSEF))
    const between = json.slice(json.indexOf(`"qr":"${URL}"`), json.indexOf(KSEF))
    expect(between).not.toContain('KSeF number')
  })

  it('AC7: every character of the name except the separator prints', () => {
    const split = texts(xmlFor({ itemName: 'Hosting / domeny / backup' }))
    expect(has(split, 'Hosting')).toBe(true)
    expect(has(split, 'domeny / backup')).toBe(true)
    const whole = texts(xmlFor({ itemName: 'Wsparcie CI/CD' }))
    expect(has(whole, 'Wsparcie CI/CD')).toBe(true)
  })

  it('AC9: countries print in both languages, large per variant; an unnamed code prints as the code', () => {
    const english = texts(xmlFor())
    expect(sizeOf(english, 'United Kingdom')).toBeGreaterThan(sizeOf(english, 'Wielka Brytania') ?? 99)
    const polish = texts(xmlFor(), 'PL-EN')
    expect(sizeOf(polish, 'Wielka Brytania')).toBeGreaterThan(sizeOf(polish, 'United Kingdom') ?? 99)
    expect(has(english, 'Poland')).toBe(true)
    expect(has(english, 'Polska')).toBe(true)
    const unnamed = texts(xmlFor({ country: 'XX' }))
    expect(unnamed.filter((item) => item.text === 'XX')).toHaveLength(1)
  })

  it('leaves out rows without XML data and rate on PLN', () => {
    const items = texts(xmlFor({ rate: '', currency: 'PLN', bank: false }))
    expect(items.some((item) => item.text.includes('Exchange rate'))).toBe(false)
    expect(has(items, 'IBAN')).toBe(false)
    expect(has(items, 'SWIFT')).toBe(false)
    expect(has(items, 'Bank')).toBe(false)
  })

  it('prints a unit without a translation once and the cash annotation when present', () => {
    const items = texts(xmlFor({ unit: 'miesiąc', extraAnnotations: true }))
    expect(items.filter((item) => item.text === 'miesiąc')).toHaveLength(1)
    expect(has(items, 'Cash accounting')).toBe(true)
    expect(has(items, 'Metoda kasowa')).toBe(true)
  })

  it('Zaplacono prints paid = total and balance 0', () => {
    const items = texts(xmlFor({ paid: true }))
    expect(items.filter((item) => item.text === '2,350.50 GBP')).toHaveLength(2)
    expect(has(items, '0.00 GBP')).toBe(true)
  })

  it('leaves the words out above the limit and refuses a broken XML', () => {
    expect(has(texts(xmlFor({ amount: '1000000000000.00' })), 'In words')).toBe(false)
    expect(() => buildPrintDocument('<nie-faktura/>', KSEF, URL, 'EN-PL')).toThrow('Nie udało się odczytać XML faktury')
    expect(() => buildPrintDocument('<Faktura', KSEF, URL, 'EN-PL')).toThrow('Nie udało się odczytać XML faktury')
  })
})
