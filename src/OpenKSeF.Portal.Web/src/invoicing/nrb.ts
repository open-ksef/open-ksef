export const digitsOnly = (value: string): string => value.replace(/\D+/g, '')

export function isValidNrb(value: string): boolean {
  const digits = digitsOnly(value)
  if (digits.length !== 26 || digits !== value.replace(/\s+/g, '')) return false
  const rearranged = `${digits.slice(2)}2521${digits.slice(0, 2)}`
  return [...rearranged].reduce((rest, digit) => (rest * 10 + Number(digit)) % 97, 0) === 1
}
