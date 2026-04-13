import type { ReactElement } from 'react'
import { Navigate } from 'react-router-dom'

export function InvoiceListPage(): ReactElement {
  return <Navigate to="/invoices/sales" replace />
}
