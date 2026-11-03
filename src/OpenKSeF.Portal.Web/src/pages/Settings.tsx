import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { oidcConfig } from '@/auth/config'
import { useAuth } from '@/auth/useAuth'
import { Button } from '@/components/Button'
import { Icon } from '@/components/Icon'

const places = [
  { to: '/company/invoice-data', title: 'Firma', sub: 'Dane do faktur, token KSeF, pobieranie' },
  { to: '/mobile-setup', title: 'Telefon', sub: 'Aplikacja mobilna i urządzenia' },
  { to: '/settings/admin', title: 'Administracja instancji', sub: 'Tylko dla osoby, która zarządza tą instancją' },
]

export function SettingsPage(): ReactElement {
  const { user, logout } = useAuth()

  return (
    <section>
      <header className="page-header">
        <h1>Ustawienia</h1>
      </header>
      <div className="company-grid">
        <section className="ui-card" aria-labelledby="account-title">
          <h2 id="account-title">Konto</h2>
          <dl className="settings-kv">
            <dt>Imię i nazwisko</dt>
            <dd data-testid="account-name">{user?.profile?.name ?? '—'}</dd>
            <dt>E-mail</dt>
            <dd data-testid="account-email">{user?.profile?.email ?? '—'}</dd>
          </dl>
          <div className="settings-actions">
            <a
              className="ui-button ui-button--outline ui-button--md"
              href={`${oidcConfig.authority}/account`}
              target="_blank"
              rel="noopener noreferrer"
              data-testid="account-manage"
            >
              Zmień hasło i dane konta
            </a>
            <Button variant="outline" data-testid="account-logout" onClick={() => void logout()}>
              <Icon name="log-out" /> Wyloguj
            </Button>
          </div>
        </section>

        <section className="ui-card" aria-labelledby="elsewhere-title">
          <h2 id="elsewhere-title">Gdzie są pozostałe ustawienia</h2>
          <ul className="start-list">
            {places.map((place) => (
              <li key={place.to}>
                <span className="start-row__main">
                  <Link to={place.to}>{place.title}</Link>
                  <span className="start-row__sub">{place.sub}</span>
                </span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </section>
  )
}
