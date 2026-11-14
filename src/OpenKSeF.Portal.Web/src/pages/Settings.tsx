import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'
import { Button } from '@/components/Button'
import { Icon } from '@/components/Icon'
import { SettingsHeader } from '@/components/SettingsHeader'
import { useSignInMethods } from '@/hooks/useSignInMethods'

const places = [
  { to: '/company/invoice-data', title: 'Firma', sub: 'Dane do faktur, token KSeF, pobieranie' },
  { to: '/mobile-setup', title: 'Telefon', sub: 'Aplikacja mobilna i urządzenia' },
  { to: '/settings/admin', title: 'Administracja instancji', sub: 'Tylko dla osoby, która zarządza tą instancją' },
]

export function SettingsPage(): ReactElement {
  const { user, logout } = useAuth()
  const { credentials, linked, password, google } = useSignInMethods()
  const methods = [password && 'E-mail i hasło', google && 'Google'].filter(Boolean).join(', ')

  return (
    <section>
      <SettingsHeader current="/settings" />
      <div className="company-grid">
        <section className="ui-card" aria-labelledby="account-title">
          <h2 id="account-title">Konto</h2>
          <dl className="settings-kv">
            <dt>Imię i nazwisko</dt>
            <dd data-testid="account-name">{user?.profile?.name ?? '—'}</dd>
            <dt>E-mail</dt>
            <dd data-testid="account-email">{user?.profile?.email ?? '—'}</dd>
            <dt>Logowanie</dt>
            <dd data-testid="account-methods">{credentials.isLoading || linked.isLoading ? '…' : methods || '—'}</dd>
          </dl>
          <div className="settings-actions">
            <Button variant="outline" data-testid="account-logout" onClick={() => void logout()}>
              <Icon name="log-out" /> Wyloguj
            </Button>
          </div>
          <p className="settings-muted">Adresu e-mail nie zmienisz w portalu. Napisz do administratora OpenKSeF.</p>
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
