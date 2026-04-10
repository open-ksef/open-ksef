// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it } from 'vitest'

import { DocumentStatusBadge } from './DocumentStatusBadge'

describe('DocumentStatusBadge', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it.each([
    ['Draft', 'Robocza', 'draft'],
    ['Approved', 'Zatwierdzona', 'approved'],
    ['SubmittedToKsef', 'Wyslana do KSeF', 'submitted'],
    ['AcceptedByKsef', 'Zaakceptowana przez KSeF', 'accepted'],
    ['RejectedByKsef', 'Odrzucona przez KSeF', 'rejected'],
  ] as const)('renders %s with Polish label and icon', async (status, label, tone) => {
    await act(async () => {
      root.render(<DocumentStatusBadge status={status} />)
    })

    const badge = container.querySelector('[role="status"]')
    const icon = container.querySelector('.invoice-status-badge__icon')

    expect(badge?.className).toContain(`invoice-status-badge--${tone}`)
    expect(badge?.getAttribute('aria-label')).toBe(label)
    expect(badge?.textContent).toContain(label)
    expect(icon?.textContent?.trim().length).toBeGreaterThan(0)
  })
})
