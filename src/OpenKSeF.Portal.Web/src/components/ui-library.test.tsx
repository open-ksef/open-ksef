// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

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
    vi.useFakeTimers()
  })

  afterEach(() => vi.useRealTimers())

  it('table skeleton shows real column headers at once and rows with one status after 300 ms', async () => {
    await act(async () => {
      root.render(<Skeleton variant="table" columns={[{ label: 'Nabywca' }, { label: 'Brutto' }]} label="Wczytywanie faktur…" />)
    })

    expect(Array.from(container.querySelectorAll('th')).map((th) => th.textContent)).toEqual(['Nabywca', 'Brutto'])
    expect(container.querySelectorAll('tbody tr').length).toBe(0)
    expect(container.querySelector('[role="status"]')?.textContent).toBe('')
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull()

    await act(async () => vi.advanceTimersByTime(300))

    expect(container.querySelectorAll('tbody tr').length).toBe(8)
    expect(container.querySelectorAll('[role="status"]').length).toBe(1)
    expect(container.querySelector('[role="status"]')?.textContent).toBe('Wczytywanie faktur…')
    expect(container.querySelector('tbody')?.getAttribute('aria-hidden')).toBe('true')
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

    await act(async () => vi.advanceTimersByTime(300))
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
