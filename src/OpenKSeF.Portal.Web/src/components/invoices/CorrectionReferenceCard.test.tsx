// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import type { CorrectionReference } from './CorrectionReferenceCard'
import { CorrectionReferenceCard } from './CorrectionReferenceCard'

const reference: CorrectionReference = {
  originalInvoiceId: '11111111-1111-1111-1111-111111111111',
  originalDocumentNumber: 'FV/2026/03/001',
  reasonKind: 'ValueChange',
  reasonDescription: 'Korekta wartości pozycji nr 1',
}

describe('CorrectionReferenceCard', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders original document number', async () => {
    await act(async () => {
      root.render(<CorrectionReferenceCard reference={reference} />)
    })

    expect(container.textContent).toContain('FV/2026/03/001')
  })

  it('renders reason kind', async () => {
    await act(async () => {
      root.render(<CorrectionReferenceCard reference={reference} />)
    })

    expect(container.textContent).toContain('ValueChange')
  })

  it('renders reason description when present', async () => {
    await act(async () => {
      root.render(<CorrectionReferenceCard reference={reference} />)
    })

    expect(container.textContent).toContain('Korekta wartości pozycji nr 1')
  })

  it('omits description when null', async () => {
    await act(async () => {
      root.render(<CorrectionReferenceCard reference={{ ...reference, reasonDescription: null }} />)
    })

    expect(container.textContent).not.toContain('Opis powodu')
  })
})
