import { useEffect, useRef, type MouseEvent } from 'react'
import { useNavigate } from 'react-router-dom'

const DOUBLE_CLICK_MS = 250

export function useRowOpen<T>(pathOf: (row: T) => string, state?: unknown): (row: T, event: MouseEvent) => void {
  const navigate = useNavigate()
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)

  useEffect(() => () => clearTimeout(timer.current), [])

  return (row, event) => {
    clearTimeout(timer.current)
    if (event.detail > 1) return
    if (event.ctrlKey || event.metaKey || event.shiftKey) {
      window.open(pathOf(row), '_blank', 'noopener')
      return
    }
    // The first click of a double click must not leave the page, or the word it selects cannot be copied.
    timer.current = setTimeout(() => {
      if (window.getSelection()?.isCollapsed === false) return
      void navigate(pathOf(row), { state })
    }, DOUBLE_CLICK_MS)
  }
}
