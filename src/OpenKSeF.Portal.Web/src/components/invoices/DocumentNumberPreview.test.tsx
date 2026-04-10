// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { DocumentNumberPreview } from './DocumentNumberPreview'

describe('DocumentNumberPreview', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders the policy-resolved document number and optional reference', async () => {
    await act(async () => {
      root.render(<DocumentNumberPreview policyResolved="FV/2026/04/001" externalReference="ERP-44" />)
    })

    expect(container.textContent).toContain('FV/2026/04/001')
    expect(container.textContent).toContain('ERP-44')
  })

  it('omits the reference line when none is provided', async () => {
    await act(async () => {
      root.render(<DocumentNumberPreview policyResolved="FV/2026/04/002" />)
    })

    expect(container.textContent).not.toContain('Referencja zewnętrzna')
  })
})
