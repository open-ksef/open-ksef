// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ErrorBanner } from './ErrorBanner'

describe('ErrorBanner', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders message and triggers retry callback', async () => {
    const onRetry = vi.fn()

    await act(async () => {
      root.render(<ErrorBanner message="Failure" onRetry={onRetry} />)
    })

    expect(container.textContent).toContain('Failure')
    ;(container.querySelector('button') as HTMLButtonElement).click()
    expect(onRetry).toHaveBeenCalledTimes(1)
  })
})
