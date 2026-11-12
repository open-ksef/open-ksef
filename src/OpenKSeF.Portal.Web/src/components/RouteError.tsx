import { useEffect, useState, type ReactElement } from 'react'
import { useLocation, useRouteError } from 'react-router-dom'

import { reloadForChunkError, shouldReloadForChunkError } from '@/loadRoute'

export function RouteError(): ReactElement {
  const { pathname } = useLocation()
  return <RouteErrorView key={pathname} pathname={pathname} />
}

function RouteErrorView({ pathname }: { pathname: string }): ReactElement {
  const error = useRouteError()
  const [reloading] = useState(() => shouldReloadForChunkError(error, pathname))

  useEffect(() => {
    if (reloading) reloadForChunkError(pathname)
  }, [reloading, pathname])

  if (reloading) {
    return <main>Ładowanie…</main>
  }

  return (
    <main className="auth-page">
      <div className="auth-card" role="alert">
        <h1 className="auth-card__title">Nie udało się wyświetlić strony</h1>
        <p className="auth-card__desc">Odśwież stronę. Jeśli błąd się powtarza, spróbuj ponownie później.</p>
        <div className="auth-card__actions">
          <button type="button" className="ui-button ui-button--primary auth-card__btn" onClick={() => location.reload()}>
            Odśwież
          </button>
        </div>
      </div>
    </main>
  )
}
