import type { ReactElement, ReactNode } from 'react'
import { Link } from 'react-router-dom'

const tabs = [
  { to: '/settings', label: 'Konto' },
  { to: '/settings/security', label: 'Bezpieczeństwo' },
  { to: '/settings/admin', label: 'Administracja' },
]

export function SettingsHeader({ current, children }: { current: string; children?: ReactNode }): ReactElement {
  return (
    <>
      <header className="page-header">
        <h1>Ustawienia</h1>
        {children}
      </header>
      <nav className="ui-tabs" aria-label="Ustawienia">
        {tabs.map((tab) => (
          <Link key={tab.to} to={tab.to} aria-current={tab.to === current ? 'page' : undefined}>
            {tab.label}
          </Link>
        ))}
      </nav>
    </>
  )
}
