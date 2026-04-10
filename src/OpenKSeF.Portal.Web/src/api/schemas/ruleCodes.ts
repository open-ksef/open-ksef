export const invoiceRuleFamilies = {
  structure: [
    'INV-VAL-001',
    'INV-VAL-002',
    'INV-VAL-003',
    'INV-VAL-030',
    'INV-VAL-031',
    'INV-VAL-032',
    'INV-VAL-040',
    'INV-VAL-041',
    'INV-VAL-042',
    'INV-VAL-050',
    'INV-VAL-051',
    'INV-VAL-052',
    'INV-VAL-053',
  ],
  parties: ['INV-VAL-010', 'INV-VAL-011', 'INV-VAL-012', 'INV-VAL-013'],
  dates: ['INV-VAL-020', 'INV-VAL-021', 'INV-VAL-022'],
  vat: ['INV-VAL-060', 'INV-VAL-061', 'INV-VAL-062', 'INV-VAL-063', 'INV-VAL-064'],
  advances: ['INV-VAL-070', 'INV-VAL-071', 'INV-VAL-072', 'INV-VAL-073'],
  correction: ['INV-VAL-080', 'INV-VAL-081', 'INV-VAL-082', 'INV-VAL-083'],
  ksef: ['INV-VAL-090', 'INV-VAL-091', 'INV-VAL-092', 'INV-VAL-093', 'INV-VAL-110', 'INV-VAL-111', 'INV-VAL-112'],
  state: ['INV-VAL-100', 'INV-VAL-101', 'INV-VAL-102'],
} as const

export type InvoiceRuleFamily = keyof typeof invoiceRuleFamilies

export const invoiceRuleCodeRegistry = Object.freeze(
  Object.entries(invoiceRuleFamilies).reduce<Record<string, InvoiceRuleFamily>>((registry, [family, codes]) => {
    for (const code of codes) {
      registry[code] = family as InvoiceRuleFamily
    }

    return registry
  }, {}),
)

export function getInvoiceRuleFamily(code: string): InvoiceRuleFamily | null {
  return invoiceRuleCodeRegistry[code] ?? null
}
