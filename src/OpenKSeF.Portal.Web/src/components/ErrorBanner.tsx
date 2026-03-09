import type { ReactElement } from 'react'

import { Button } from './Button'

interface ErrorBannerProps {
  message: string
  onRetry?: () => void
}

export function ErrorBanner({ message, onRetry }: ErrorBannerProps): ReactElement {
  return (
    <section className="ui-error-banner" role="alert">
      <span>{message}</span>
      {onRetry ? <Button variant="outline" size="sm" onClick={onRetry}>Retry</Button> : null}
    </section>
  )
}
