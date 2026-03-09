// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Button } from './Button'
import { EmptyState } from './EmptyState'
import { ErrorBanner } from './ErrorBanner'
import { FilterBar } from './FilterBar'
import { Skeleton } from './Skeleton'
import { StatusBadge } from './StatusBadge'
import { Table } from './Table'

describe('UI component library', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders table data and triggers row click handler', async () => {
    const onRowClick = vi.fn<(row: { id: string; name: string }) => void>()

    await act(async () => {
      root.render(
        <Table
          testId="sample-table"
          columns={[{ key: 'name', label: 'Name' }]}
          data={[{ id: '1', name: 'Tenant A' }]}
          onRowClick={onRowClick}
        />,
      )
    })

    expect(container.querySelector('[data-testid="sample-table"]')).toBeTruthy()
    const row = container.querySelector('tbody tr') as HTMLTableRowElement
    row.click()
    expect(onRowClick).toHaveBeenCalledWith({ id: '1', name: 'Tenant A' })
  })

  it('runs filter apply and reset actions', async () => {
    const onApply = vi.fn()
    const onReset = vi.fn()

    await act(async () => {
      root.render(
        <FilterBar onApply={onApply} onReset={onReset}>
          <input aria-label="Query" />
        </FilterBar>,
      )
    })

    const buttons = container.querySelectorAll('button')
    ;(buttons[0] as HTMLButtonElement).click()
    ;(buttons[1] as HTMLButtonElement).click()

    expect(onApply).toHaveBeenCalledTimes(1)
    expect(onReset).toHaveBeenCalledTimes(1)
  })

  it('renders status, skeleton, empty state and error banner', async () => {
    const onRetry = vi.fn()
    const onAction = vi.fn()

    await act(async () => {
      root.render(
        <>
          <StatusBadge status="warning" label="Needs attention" />
          <Skeleton lines={2} />
          <EmptyState title="No tenants" message="Create a tenant to continue" action={{ label: 'Create', onClick: onAction }} />
          <ErrorBanner message="Unable to load data" onRetry={onRetry} />
          <Button variant="outline" size="sm" onClick={onAction}>Test button</Button>
        </>,
      )
    })

    expect(container.textContent).toContain('Needs attention')
    expect(container.querySelectorAll('.skeleton-line').length).toBe(2)

    const createButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Create')
    createButton?.click()

    const retryButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Retry')
    retryButton?.click()

    const testButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Test button')
    testButton?.click()

    expect(onAction).toHaveBeenCalledTimes(2)
    expect(onRetry).toHaveBeenCalledTimes(1)
  })
})
