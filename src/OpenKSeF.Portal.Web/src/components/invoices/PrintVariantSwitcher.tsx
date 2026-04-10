import type { ChangeEvent, ReactElement } from 'react'

type PrintVariant = 'Standard' | 'Duplicate' | 'English'

const variantLabels: Record<PrintVariant, string> = {
  Standard: 'Standardowy',
  Duplicate: 'Duplikat',
  English: 'Angielski',
}

interface PrintVariantSwitcherProps {
  variant: PrintVariant
  onChange: (variant: PrintVariant) => void
  disabledVariants?: PrintVariant[]
}

export function PrintVariantSwitcher({
  variant,
  onChange,
  disabledVariants = [],
}: PrintVariantSwitcherProps): ReactElement {
  const variants: PrintVariant[] = ['Standard', 'Duplicate', 'English']

  return (
    <div className="print-variant-switcher" role="radiogroup" aria-label="Wariant wydruku">
      {variants.map((v) => (
        <label key={v} className="print-variant-switcher__option">
          <input
            type="radio"
            name="printVariant"
            value={v}
            checked={variant === v}
            disabled={disabledVariants.includes(v)}
            onChange={(e: ChangeEvent<HTMLInputElement>) => {
              if (e.target.checked) {
                onChange(v)
              }
            }}
            onClick={() => onChange(v)}
          />
          <span>{variantLabels[v]}</span>
        </label>
      ))}
    </div>
  )
}
