import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

export function NotFoundPage(): ReactElement {
  return (
    <main className="auth-page">
      <div className="auth-card" style={{ textAlign: 'center' }}>
        <div style={{ fontSize: '48px', marginBottom: '8px' }}>404</div>
        <h1 className="auth-card__title">Nie znaleziono strony</h1>
        <p className="auth-card__desc">Strona, której szukasz, nie istnieje lub została przeniesiona.</p>
        <div className="auth-card__actions">
          <Link to="/" className="ui-button ui-button--primary auth-card__btn">
            Wróć na stronę główną
          </Link>
        </div>
      </div>
    </main>
  )
}
