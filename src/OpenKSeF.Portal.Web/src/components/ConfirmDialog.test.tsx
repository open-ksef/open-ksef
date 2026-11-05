// @vitest-environment jsdom
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ConfirmDialog } from './ConfirmDialog'

describe('ConfirmDialog focus', () => {
  let root: Root
  let container: HTMLDivElement
  let opener: HTMLButtonElement

  const dialog = (open: boolean, isPending = false): ReactElement => (
    <ConfirmDialog open={open} title="T" message="M" isPending={isPending} onConfirm={vi.fn()} onCancel={vi.fn()} />
  )
  const buttons = (): HTMLButtonElement[] => [...container.querySelectorAll('button')]
  const tab = (shiftKey = false): boolean => {
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
    ;(document.activeElement ?? document.body).dispatchEvent(event)
    return event.defaultPrevented
  }

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    opener = document.body.appendChild(document.createElement('button'))
    opener.focus()
    container = document.body.appendChild(document.createElement('div'))
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    opener.remove()
  })

  it('starts on cancel, wraps at both edges, restores the opener', async () => {
    await act(async () => root.render(dialog(true)))
    const [close, cancel, confirm] = buttons()
    expect(document.activeElement).toBe(cancel)

    expect(tab()).toBe(false)
    confirm.focus()
    expect(tab()).toBe(true)
    expect(document.activeElement).toBe(close)
    expect(tab(true)).toBe(true)
    expect(document.activeElement).toBe(confirm)

    await act(async () => root.render(dialog(false)))
    expect(document.activeElement).toBe(opener)
  })

  it('keeps Tab inside when the pending confirm button lost focus', async () => {
    await act(async () => root.render(dialog(true)))
    await act(async () => root.render(dialog(true, true)))
    ;(document.activeElement as HTMLElement).blur()
    expect(document.activeElement).toBe(document.body)

    expect(tab()).toBe(true)
    expect(document.activeElement).toBe(buttons()[0])
  })
})
