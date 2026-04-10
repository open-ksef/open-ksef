// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { KsefSubmissionStatus } from './KsefSubmissionStatus'

describe('KsefSubmissionStatus', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it.each([
    ['NotPlanned', 'Brak wysyłki do KSeF', 'neutral'],
    ['Ready', 'Gotowa do wysyłki', 'ready'],
    ['Submitted', 'Wysłana do KSeF', 'submitted'],
    ['Accepted', 'Przyjęta przez KSeF', 'accepted'],
    ['Rejected', 'Odrzucona przez KSeF', 'rejected'],
  ] as const)('renders %s with a distinct visual state', async (state, label, tone) => {
    await act(async () => {
      root.render(<KsefSubmissionStatus state={state} />)
    })

    const banner = container.querySelector('[role="status"]')

    expect(banner?.className).toContain(`ksef-submission-status--${tone}`)
    expect(banner?.getAttribute('aria-label')).toBe(label)
    expect(banner?.textContent).toContain(label)
  })

  it('renders acceptance identifiers and rejection reason details when provided', async () => {
    await act(async () => {
      root.render(
        <KsefSubmissionStatus
          state="Rejected"
          identifiers={{ ksefDocumentNumber: 'KSEF-1', ksefReferenceNumber: 'REF-1' }}
          rejectionReason="Walidacja techniczna odrzucona."
        />,
      )
    })

    expect(container.textContent).toContain('KSEF-1')
    expect(container.textContent).toContain('REF-1')
    expect(container.textContent).toContain('Walidacja techniczna odrzucona.')
  })
})
