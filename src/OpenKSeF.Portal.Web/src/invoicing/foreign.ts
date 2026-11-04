import type { SellerProfileResponse, VatRateCode } from '@/api/types'

const EU_COUNTRIES = new Set('AT BE BG CY CZ DE DK EE ES FI FR GR HR HU IE IT LT LU LV MT NL PT RO SE SI SK'.split(' '))

const COUNTRY_CODES =
  'AF AX AL DZ AD AO AI AQ AG AN SA AR AM AW AU AT AZ BS BH BD BB BE BZ BJ BM BT BY BO BQ BA BW BR BN IO BG BF BI XC CL CN HR CW CY TD ME DK DM DO DJ EG EC ER EE ET FK FJ PH FI FR TF GA GM GH GI GR GD GL GE GU GG GY GF GP GT GN GQ GW HT ES HN HK IN ID IQ IR IE IS IL JM JP YE JE JO KY KH CM CA QA KZ KE KG KI CO KM CG CD KP XK CR CU KW LA LS LB LR LY LI LT LV LU MK MG YT MO MW MV MY ML MT MP MA MQ MR MU MX XL FM UM MD MC MN MS MZ MM NA NR NP NL DE NE NG NI NU NF NO NC NZ PS OM PK PW PA PG PY PE PN PF GS PT PR CF CZ KR ZA RE RU RO RW EH BL KN LC MF VC SV WS AS SM SN RS SC SL SG SK SI SO LK PM US SZ SD SS SR SJ SH SY CH SE TJ TH TW TZ TG TK TO TT TN TR TM TV UG UA UY UZ VU WF VA HU VE GB VN IT TL CI BV CX IM SX CK VI VG HM CC MH FO SB ST TC ZM CV ZW AE XI'.split(
    ' ',
  )

const MAIN_CURRENCIES = ['PLN', 'EUR', 'USD', 'GBP', 'CHF']

const OTHER_CURRENCIES =
  'AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BRL BSD BTN BWP BYN BZD CAD CDF CLP CNY COP CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB FJD FKP GEL GGP GHS GIP GMD GNF GTQ GYD HKD HNL HRK HTG HUF IDR ILS IMP INR IQD IRR ISK JEP JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP SLL SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX UYU UZS VES VND VUV WST XAF XCD XCG XOF XPF YER ZAR ZMW ZWL'.split(
    ' ',
  )

export const CURRENCIES = [...MAIN_CURRENCIES, ...OTHER_CURRENCIES]

const regionNames = {
  pl: new Intl.DisplayNames(['pl'], { type: 'region', fallback: 'none' }),
  en: new Intl.DisplayNames(['en'], { type: 'region', fallback: 'none' }),
}

export const countryName = (code: string, language: 'pl' | 'en' = 'pl'): string => regionNames[language].of(code) ?? code

export const COUNTRY_OPTIONS = COUNTRY_CODES.map((code) => ({ code, name: countryName(code) })).sort((a, b) =>
  a.name.localeCompare(b.name, 'pl'),
)

export const isEuCountry = (code: string): boolean => EU_COUNTRIES.has(code)

export const foreignRateFor = (countryCode: string): VatRateCode => (isEuCountry(countryCode) ? 'np II' : 'np I')

export interface ForeignAccount {
  iban: string
  swift: string | null
  bankName: string | null
}

export function foreignAccountOf(profile: SellerProfileResponse): ForeignAccount | null {
  if (profile.foreignBankAccount) {
    return { iban: profile.foreignBankAccount, swift: profile.foreignBankSwift ?? null, bankName: profile.foreignBankName ?? null }
  }
  return profile.bankAccount ? { iban: `PL${profile.bankAccount}`, swift: profile.bankSwift ?? null, bankName: profile.bankName ?? null } : null
}
