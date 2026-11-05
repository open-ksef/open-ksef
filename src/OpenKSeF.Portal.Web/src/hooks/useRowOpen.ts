import { useEffect, useRef, type MouseEvent } from 'react'
import { useNavigate } from 'react-router-dom'

const DOUBLE_CLICK_MS = 250

// Chromium and WebKit select no text inside a link on a double click, and the row name is what users copy.
export function selectWordAt({ clientX, clientY }: MouseEvent): void {
  const caret = document.caretRangeFromPoint?.(clientX, clientY)
  if (caret?.startContainer.nodeType !== Node.TEXT_NODE) return
  const { startContainer: node, startOffset: offset } = caret
  const word = [...new Intl.Segmenter('pl', { granularity: 'word' }).segment(node.textContent ?? '')].filter(({ index }) => index <= offset).pop()
  if (word) window.getSelection()?.setBaseAndExtent(node, word.index, node, word.index + word.segment.length)
}

export function useRowOpen<T>(pathOf: (row: T) => string, state?: unknown): (row: T, event: MouseEvent) => void {
  const navigate = useNavigate()
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)

  useEffect(() => () => clearTimeout(timer.current), [])

  return (row, event) => {
    clearTimeout(timer.current)
    if (event.detail > 1) return
    if (event.ctrlKey || event.metaKey || event.shiftKey) {
      // No noopener: the OIDC user is in sessionStorage, and only a tab with an opener gets a copy of it.
      window.open(pathOf(row), '_blank')
      return
    }
    if (event.detail === 0) {
      void navigate(pathOf(row), { state })
      return
    }
    // A click on a link does not collapse a selection, and a drag selection never ends with a click whose target is a link.
    const onLink = event.target instanceof Element && event.target.closest('a') !== null
    // The first click of a double click must not leave the page, or the word it selects cannot be copied.
    timer.current = setTimeout(() => {
      if (!onLink && window.getSelection()?.isCollapsed === false) return
      void navigate(pathOf(row), { state })
    }, DOUBLE_CLICK_MS)
  }
}
