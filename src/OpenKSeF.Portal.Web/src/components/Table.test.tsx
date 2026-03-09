// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Table } from './Table'

describe('Table', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders columns and rows and handles clicks', async () => {
    const onRowClick = vi.fn<(row: { id: string; name: string }) => void>()

    await act(async () => {
      root.render(
        <Table
          columns={[{ key: 'name', label: 'Name' }]}
          data={[{ id: '1', name: 'Tenant A' }]}
          onRowClick={onRowClick}
          testId="table-test"
        />,
      )
    })

    expect(container.querySelector('[data-testid="table-test"]')).toBeTruthy()
    expect(container.textContent).toContain('Name')
    expect(container.textContent).toContain('Tenant A')

    ;(container.querySelector('tbody tr') as HTMLTableRowElement).click()
    expect(onRowClick).toHaveBeenCalledWith({ id: '1', name: 'Tenant A' })
  })
})
