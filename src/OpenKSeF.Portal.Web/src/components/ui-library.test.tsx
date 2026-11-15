// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Button } from './Button'
import { EmptyState } from './EmptyState'
import { ErrorBanner } from './ErrorBanner'
import { Skeleton } from './Skeleton'

describe('UI component library', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders skeleton, empty state and error banner', async () => {
    const onRetry = vi.fn()
    const onAction = vi.fn()

    await act(async () => {
      root.render(
        <>
          <Skeleton lines={2} />
          <EmptyState title="No tenants" message="Create a tenant to continue" action={{ label: 'Create', onClick: onAction }} />
          <ErrorBanner message="Unable to load data" onRetry={onRetry} />
          <Button variant="outline" size="sm" onClick={onAction}>Test button</Button>
        </>,
      )
    })

    expect(container.querySelectorAll('.skeleton-line').length).toBe(2)

    const createButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Create')
    createButton?.click()

    const retryButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Spróbuj ponownie')
    retryButton?.click()

    const testButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Test button')
    testButton?.click()

    expect(onAction).toHaveBeenCalledTimes(2)
    expect(onRetry).toHaveBeenCalledTimes(1)
  })
})
