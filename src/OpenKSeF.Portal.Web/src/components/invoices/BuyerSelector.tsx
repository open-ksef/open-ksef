import { useQuery } from '@tanstack/react-query'
import { useId, useRef, useState, type ChangeEvent, type KeyboardEvent, type ReactElement } from 'react'

import { listAggregateInvoices } from '@/api/invoicesAggregateApi'
import type { BuyerKind } from '@/api/schemas/invoice'

export interface BuyerSuggestion {
  name: string
  nip: string | null
  buyerKind: BuyerKind
}

interface BuyerSelectorProps {
  value: string
  onChange: (value: string) => void
  onSelect: (suggestion: BuyerSuggestion) => void
  tenantId: string
}

export function BuyerSelector({ value, onChange, onSelect, tenantId }: BuyerSelectorProps): ReactElement {
  const listboxId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)

  const buyersQuery = useQuery({
    queryKey: ['buyer-suggestions', tenantId],
    queryFn: () => listAggregateInvoices(tenantId, { pageSize: 50 }),
    enabled: Boolean(tenantId),
    staleTime: 60_000,
  })

  const suggestions: BuyerSuggestion[] = []
  if (buyersQuery.data) {
    const seen = new Set<string>()
    for (const invoice of buyersQuery.data.items) {
      const nip = invoice.buyer.nip
      const key = nip ?? invoice.buyer.name
      if (!seen.has(key)) {
        seen.add(key)
        suggestions.push({ name: invoice.buyer.name, nip, buyerKind: invoice.buyerKind })
      }
    }
  }

  const filtered = value.trim()
    ? suggestions.filter((s) => s.name.toLowerCase().includes(value.toLowerCase()) || (s.nip ?? '').includes(value))
    : suggestions

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    onChange(event.target.value)
    setOpen(true)
    setActiveIndex(-1)
  }

  function handleSelect(suggestion: BuyerSuggestion) {
    onSelect(suggestion)
    setOpen(false)
    setActiveIndex(-1)
    inputRef.current?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!open || filtered.length === 0) {
      if (event.key === 'ArrowDown' && filtered.length > 0) {
        setOpen(true)
        setActiveIndex(0)
        event.preventDefault()
      }
      return
    }

    if (event.key === 'ArrowDown') {
      setActiveIndex((i) => Math.min(i + 1, filtered.length - 1))
      event.preventDefault()
    } else if (event.key === 'ArrowUp') {
      setActiveIndex((i) => Math.max(i - 1, 0))
      event.preventDefault()
    } else if (event.key === 'Enter' && activeIndex >= 0) {
      handleSelect(filtered[activeIndex])
      event.preventDefault()
    } else if (event.key === 'Escape') {
      setOpen(false)
      setActiveIndex(-1)
    }
  }

  return (
    <div className="buyer-selector">
      <input
        ref={inputRef}
        id="buyer-name"
        data-testid="buyer-name"
        type="text"
        role="combobox"
        aria-expanded={open && filtered.length > 0}
        aria-controls={listboxId}
        aria-activedescendant={activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
        aria-autocomplete="list"
        value={value}
        onChange={handleChange}
        onInput={(event) => { onChange(event.currentTarget.value); setOpen(true); setActiveIndex(-1) }}
        onKeyDown={handleKeyDown}
        onFocus={() => { if (filtered.length > 0) setOpen(true) }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      />
      {open && filtered.length > 0 && (
        <ul
          id={listboxId}
          role="listbox"
          className="buyer-selector__dropdown"
        >
          {filtered.map((suggestion, index) => (
            <li
              key={suggestion.nip ?? suggestion.name}
              id={`${listboxId}-option-${index}`}
              role="option"
              aria-selected={index === activeIndex}
              className={`buyer-selector__option${index === activeIndex ? ' buyer-selector__option--active' : ''}`}
              onMouseDown={(event) => { event.preventDefault(); handleSelect(suggestion) }}
            >
              <span className="buyer-selector__option-name">{suggestion.name}</span>
              {suggestion.nip && <span className="buyer-selector__option-nip">{suggestion.nip}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
