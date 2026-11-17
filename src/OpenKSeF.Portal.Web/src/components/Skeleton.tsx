import { useEffect, useState, type ReactElement } from 'react'

const SHOW_DELAY_MS = 300

interface SkeletonColumn {
  label: string
  className?: string
}

interface SkeletonProps {
  lines?: number
  variant?: 'lines' | 'table' | 'card'
  columns?: SkeletonColumn[]
  rows?: number
  label?: string
}

const barWidth = (row: number, col: number): string => `${40 + ((row * 17 + col * 29) % 31)}%`

export function Skeleton({ lines = 3, variant = 'lines', columns = [], rows = 8, label = 'Wczytywanie…' }: SkeletonProps): ReactElement {
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const timer = setTimeout(() => setShown(true), SHOW_DELAY_MS)
    return () => clearTimeout(timer)
  }, [])

  const status = (
    <span role="status" className="visually-hidden">
      {shown ? label : ''}
    </span>
  )

  if (variant === 'table') {
    return (
      <section className="received-list ui-skeleton-table" aria-label={label}>
        {status}
        <div className="ui-table-wrap" aria-busy="true">
          <table className="ui-table">
            <thead>
              <tr>
                {columns.map((column) => (
                  <th key={column.label} scope="col" className={column.className}>
                    {column.label}
                  </th>
                ))}
              </tr>
            </thead>
            {shown ? (
              <tbody aria-hidden="true">
                {Array.from({ length: rows }).map((_, row) => (
                  <tr key={row}>
                    {columns.map((column, col) => (
                      <td key={column.label} className={column.className}>
                        <div className="skeleton-line" style={{ width: barWidth(row, col) }} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            ) : null}
          </table>
        </div>
      </section>
    )
  }

  return (
    <div className={variant === 'card' ? 'ui-skeleton ui-skeleton--card' : 'ui-skeleton'} aria-busy="true">
      {status}
      {shown ? (
        <>
          {variant === 'card' ? <div className="skeleton-line skeleton-line--title" aria-hidden="true" /> : null}
          {Array.from({ length: lines }).map((_, index) => (
            <div
              key={index}
              className="skeleton-line"
              aria-hidden="true"
              style={variant === 'card' ? { width: barWidth(index, 1) } : undefined}
            />
          ))}
        </>
      ) : null}
    </div>
  )
}
