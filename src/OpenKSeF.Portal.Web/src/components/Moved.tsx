import type { ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

export function Moved({ to }: { to: string }): ReactElement {
  const { search } = useLocation()
  return <Navigate to={{ pathname: to, search }} replace />
}
