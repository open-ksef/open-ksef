import type { SellerProfileRequest, SellerProfileResponse } from '@/api/types'
import { formatNrb } from './format'

export interface SellerProfileFormValue {
  legalName: string
  addressLine1: string
  addressLine2: string
  bankAccount: string
  cashAccounting: boolean
  numberingPattern: string
  nextNumber: string
}

export const NUMBER_TOKENS = ['{nr}', '{MM}', '{RRRR}'] as const

export const digitsOnly = (value: string): string => value.replace(/\D+/g, '')

export function isValidNrb(value: string): boolean {
  const digits = digitsOnly(value)
  if (digits.length !== 26 || digits !== value.replace(/\s+/g, '')) return false
  const rearranged = `${digits.slice(2)}2521${digits.slice(0, 2)}`
  return [...rearranged].reduce((rest, digit) => (rest * 10 + Number(digit)) % 97, 0) === 1
}

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
  return errors
}

export function toSellerProfileRequest(
  form: SellerProfileFormValue,
  profile: SellerProfileResponse,
  today: Date,
): SellerProfileRequest {
  const text = (value: string): string | null => value.trim() || null
  const numberChanged = Number(form.nextNumber) !== profile.nextNumber
  return {
    legalName: text(form.legalName),
    addressLine1: text(form.addressLine1),
    addressLine2: text(form.addressLine2),
    bankAccount: text(digitsOnly(form.bankAccount)),
    cashAccounting: form.cashAccounting,
    numberingPattern: form.numberingPattern,
    ...(numberChanged
      ? { nextNumber: Number(form.nextNumber), nextNumberPeriod: numberingPeriod(form.numberingPattern, today) }
      : {}),
  }
}
