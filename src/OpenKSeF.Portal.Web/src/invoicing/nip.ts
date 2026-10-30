const NIP_WEIGHTS = [6, 5, 7, 2, 3, 4, 5, 6, 7]

export function isValidNip(nip: string): boolean {
  if (!/^\d{10}$/.test(nip)) {
    return false
  }

  const sum = NIP_WEIGHTS.reduce((acc, weight, index) => acc + weight * Number(nip[index]), 0)
  return sum % 11 === Number(nip[9])
}
