import type { ReactElement } from 'react'

export function BrandMark({ size }: { size: number }): ReactElement {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
      <path
        d="M20 2H10a8 8 0 0 0-8 8v12a8 8 0 0 0 8 8h12a8 8 0 0 0 8-8V12h-6v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2h10zM22 2v8h8z"
        fill="currentColor"
      />
    </svg>
  )
}
