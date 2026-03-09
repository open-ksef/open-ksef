import type { ReactElement } from 'react'

interface SkeletonProps {
  lines?: number
}

export function Skeleton({ lines = 3 }: SkeletonProps): ReactElement {
  return (
    <div className="ui-skeleton" aria-busy="true" aria-live="polite">
      {Array.from({ length: lines }).map((_, index) => (
        <div key={index} className="skeleton-line" />
      ))}
    </div>
  )
}
