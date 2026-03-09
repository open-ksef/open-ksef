import type { ReactElement, ReactNode } from 'react'

export interface TableColumn<T> {
  key: keyof T
  label: string
  render?: (row: T) => ReactNode
}

interface TableProps<T extends object> {
  columns: TableColumn<T>[]
  data: T[]
  onRowClick?: (row: T) => void
  testId?: string
  getRowProps?: (row: T, index: number) => Record<string, string | number | boolean | undefined>
}

export function Table<T extends object>({
  columns,
  data,
  onRowClick,
  testId,
  getRowProps,
}: TableProps<T>): ReactElement {
  return (
    <div className="ui-table-wrap" data-testid={testId}>
      <table className="ui-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={String(column.key)}>{column.label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {data.map((row, rowIndex) => {
            const rowProps = getRowProps ? getRowProps(row, rowIndex) : undefined
            const rowId = (row as { id?: string | number }).id
            return (
              <tr key={String(rowId ?? rowIndex)} onClick={() => onRowClick?.(row)} {...rowProps}>
              {columns.map((column) => (
                <td key={`${String(column.key)}-${rowIndex}`}>
                  {column.render ? column.render(row) : String(row[column.key] ?? '')}
                </td>
              ))}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
