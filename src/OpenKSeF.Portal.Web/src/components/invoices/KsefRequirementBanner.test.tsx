// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { KsefRequirementBanner } from './KsefRequirementBanner'

describe('KsefRequirementBanner', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it.each([
    ['Required', 'Wysyłka do KSeF jest wymagana', 'required'],
    ['Optional', 'Wysyłka do KSeF jest opcjonalna', 'optional'],
    ['Forbidden', 'Wysyłka do KSeF jest niedozwolona', 'forbidden'],
    ['NotApplicable', 'KSeF nie ma zastosowania', 'na'],
  ] as const)('renders %s requirement with note semantics', async (requirement, title, tone) => {
    await act(async () => {
      root.render(<KsefRequirementBanner requirement={requirement} />)
    })

    const banner = container.querySelector('[role="note"]')

    expect(banner?.className).toContain(`ksef-requirement-banner--${tone}`)
    expect(banner?.textContent).toContain(title)
  })
})
