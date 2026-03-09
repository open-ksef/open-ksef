import type { ReactElement, ReactNode } from 'react'

import { EmptyState } from './EmptyState'
import { ErrorBanner } from './ErrorBanner'
import { Skeleton } from './Skeleton'

interface AsyncStateViewProps {
  isLoading: boolean
  error: unknown
  isEmpty: boolean
  loadingLines?: number
  emptyTitle: string
  emptyMessage: string
  onRetry: () => void
  children: ReactNode
}

export function AsyncStateView({
  isLoading,
  error,
  isEmpty,
  loadingLines = 5,
  emptyTitle,
  emptyMessage,
  onRetry,
  children,
}: AsyncStateViewProps): ReactElement {
  if (isLoading) {
    return <Skeleton lines={loadingLines} />
  }

  if (error) {
    return <ErrorBanner message={error instanceof Error ? error.message : 'Nie udało się załadować danych'} onRetry={onRetry} />
  }

  if (isEmpty) {
    return <EmptyState title={emptyTitle} message={emptyMessage} />
  }

  return <>{children}</>
}
