// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AsyncStateView } from './AsyncStateView'

describe('AsyncStateView', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders loading, error, empty and success states', async () => {
    const onRetry = vi.fn()

    await act(async () => {
      root.render(
        <AsyncStateView
          isLoading
          error={null}
          isEmpty={false}
          emptyTitle="Empty"
          emptyMessage="No data"
          onRetry={onRetry}
        >
          <div>Success</div>
        </AsyncStateView>,
      )
    })
    expect(container.querySelector('.skeleton-line')).toBeTruthy()

    await act(async () => {
      root.render(
        <AsyncStateView
          isLoading={false}
          error={new Error('boom')}
          isEmpty={false}
          emptyTitle="Empty"
          emptyMessage="No data"
          onRetry={onRetry}
        >
          <div>Success</div>
        </AsyncStateView>,
      )
    })
    expect(container.textContent).toContain('boom')

    await act(async () => {
      root.render(
        <AsyncStateView
          isLoading={false}
          error={null}
          isEmpty
          emptyTitle="Empty"
          emptyMessage="No data"
          onRetry={onRetry}
        >
          <div>Success</div>
        </AsyncStateView>,
      )
    })
    expect(container.textContent).toContain('Empty')

    await act(async () => {
      root.render(
        <AsyncStateView
          isLoading={false}
          error={null}
          isEmpty={false}
          emptyTitle="Empty"
          emptyMessage="No data"
          onRetry={onRetry}
        >
          <div>Success</div>
        </AsyncStateView>,
      )
    })
    expect(container.textContent).toContain('Success')
  })
})
