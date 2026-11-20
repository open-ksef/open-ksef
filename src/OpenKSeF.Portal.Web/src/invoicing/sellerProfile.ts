import type { SellerProfileRequest, SellerProfileResponse } from '@/api/types'
import { formatNrb } from './format'
import { digitsOnly, isValidNrb } from './nrb'

export { digitsOnly, isValidNrb }

export interface SellerProfileFormValue {
  legalName: string
  addressLine1: string
  addressLine2: string
  bankAccount: string
  cashAccounting: boolean
  numberingPattern: string
  nextNumber: string
  correctionNumberingPattern: string
  correctionNextNumber: string
  advanceNumberingPattern: string
  advanceNextNumber: string
  bankSwift: string
  bankName: string
  foreignBankAccount: string
  foreignBankSwift: string
  foreignBankName: string
}

export const DEFAULT_CORRECTION_PATTERN = 'KOR/{nr}/{MM}/{RRRR}'
export const DEFAULT_ADVANCE_PATTERN = 'ZAL/{nr}/{MM}/{RRRR}'

export const NUMBER_TOKENS = ['{nr}', '{MM}', '{RRRR}'] as const

export function numberingPeriod(pattern: string, today: Date): string {
  const year = String(today.getFullYear())
  return pattern.includes('{MM}') ? `${year}-${String(today.getMonth() + 1).padStart(2, '0')}` : year
}

export function previewNumber(pattern: string, nextNumber: number, today: Date): string {
  return pattern
    .replaceAll('{nr}', String(nextNumber))
    .replaceAll('{MM}', String(today.getMonth() + 1).padStart(2, '0'))
    .replaceAll('{RRRR}', String(today.getFullYear()))
}

export function fromProfile(profile: SellerProfileResponse): SellerProfileFormValue {
  return {
    legalName: profile.legalName ?? '',
    addressLine1: profile.addressLine1 ?? '',
    addressLine2: profile.addressLine2 ?? '',
    bankAccount: profile.bankAccount ? formatNrb(profile.bankAccount) : '',
    cashAccounting: profile.cashAccounting,
    numberingPattern: profile.numberingPattern,
    nextNumber: String(profile.nextNumber),
    correctionNumberingPattern: profile.correctionNumberingPattern ?? DEFAULT_CORRECTION_PATTERN,
    correctionNextNumber: String(profile.correctionNextNumber ?? 1),
    advanceNumberingPattern: profile.advanceNumberingPattern ?? DEFAULT_ADVANCE_PATTERN,
    advanceNextNumber: String(profile.advanceNextNumber ?? 1),
    bankSwift: profile.bankSwift ?? '',
    bankName: profile.bankName ?? '',
    foreignBankAccount: profile.foreignBankAccount ?? '',
    foreignBankSwift: profile.foreignBankSwift ?? '',
    foreignBankName: profile.foreignBankName ?? '',
  }
}

export function validateSellerProfile(form: SellerProfileFormValue): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!form.legalName.trim()) errors.legalName = 'Uzupełnij nazwę firmy'
  if (!form.addressLine1.trim()) errors.addressLine1 = 'Uzupełnij adres firmy'
  if (form.bankAccount.trim() && !isValidNrb(form.bankAccount)) errors.bankAccount = 'Niepoprawny numer rachunku'
  if (!form.numberingPattern.includes('{nr}')) errors.numberingPattern = 'Wzorzec numeracji musi zawierać {nr}'
  if (!/^\d+$/.test(form.nextNumber) || Number(form.nextNumber) < 1) {
    errors.nextNumber = 'Następny numer musi być większy od zera'
  }
  if (!form.correctionNumberingPattern.includes('{nr}')) {
    errors.correctionNumberingPattern = 'Wzorzec numeracji musi zawierać {nr}'
  } else if (form.correctionNumberingPattern.trim() === form.numberingPattern.trim()) {
    errors.correctionNumberingPattern = 'Wzorzec korekt musi różnić się od wzorca faktur'
  }
  if (!/^\d+$/.test(form.correctionNextNumber) || Number(form.correctionNextNumber) < 1) {
    errors.correctionNextNumber = 'Następny numer musi być większy od zera'
  }
  if (!form.advanceNumberingPattern.includes('{nr}')) {
    errors.advanceNumberingPattern = 'Wzorzec numeracji musi zawierać {nr}'
  } else if ([form.numberingPattern.trim(), form.correctionNumberingPattern.trim()].includes(form.advanceNumberingPattern.trim())) {
    errors.advanceNumberingPattern = 'Wzorzec zaliczek musi różnić się od wzorców faktur i korekt'
  }
  if (!/^\d+$/.test(form.advanceNextNumber) || Number(form.advanceNextNumber) < 1) {
    errors.advanceNextNumber = 'Następny numer musi być większy od zera'
  }
  return errors
}

export function toSellerProfileRequest(
  form: SellerProfileFormValue,
  profile: SellerProfileResponse,
  today: Date,
): SellerProfileRequest {
  const text = (value: string): string | null => value.trim() || null
  const numberChanged = Number(form.nextNumber) !== profile.nextNumber
  const correctionNumberChanged = Number(form.correctionNextNumber) !== (profile.correctionNextNumber ?? 1)
  const advanceNumberChanged = Number(form.advanceNextNumber) !== (profile.advanceNextNumber ?? 1)
  return {
    legalName: text(form.legalName),
    addressLine1: text(form.addressLine1),
    addressLine2: text(form.addressLine2),
    bankAccount: text(digitsOnly(form.bankAccount)),
    cashAccounting: form.cashAccounting,
    numberingPattern: form.numberingPattern,
    correctionNumberingPattern: form.correctionNumberingPattern,
    advanceNumberingPattern: form.advanceNumberingPattern,
    bankSwift: form.bankSwift.trim(),
    bankName: form.bankName.trim(),
    foreignBankAccount: form.foreignBankAccount.trim(),
    foreignBankSwift: form.foreignBankSwift.trim(),
    foreignBankName: form.foreignBankName.trim(),
    ...(numberChanged
      ? { nextNumber: Number(form.nextNumber), nextNumberPeriod: numberingPeriod(form.numberingPattern, today) }
      : {}),
    ...(correctionNumberChanged
      ? {
          correctionNextNumber: Number(form.correctionNextNumber),
          correctionNextNumberPeriod: numberingPeriod(form.correctionNumberingPattern, today),
        }
      : {}),
    ...(advanceNumberChanged
      ? {
          advanceNextNumber: Number(form.advanceNextNumber),
          advanceNextNumberPeriod: numberingPeriod(form.advanceNumberingPattern, today),
        }
      : {}),
  }
}
