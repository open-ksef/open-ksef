import type { ReactElement } from 'react'
import { Link, Outlet, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'
import { CompanyCard } from '@/company/CompanyCard'
import { Icon, type IconName } from './Icon'

const navItems: { to: string; label: string; icon: IconName; alsoOn?: string[] }[] = [
  { to: '/', label: 'Start', icon: 'house' },
  { to: '/invoices', label: 'Otrzymane', icon: 'inbox' },
  { to: '/issued', label: 'Wystawione', icon: 'send' },
  { to: '/company', label: 'Firma', icon: 'building-2', alsoOn: ['/seller-profile', '/credentials', '/tenants'] },
  { to: '/mobile-setup', label: 'Telefon', icon: 'smartphone', alsoOn: ['/devices'] },
]

const isUnder = (pathname: string, base: string): boolean =>
  base === '/' ? pathname === '/' : pathname === base || pathname.startsWith(`${base}/`)

export function Layout(): ReactElement {
  const { user, logout } = useAuth()
  const { pathname } = useLocation()

  const displayName = user?.profile?.name ?? user?.profile?.email ?? 'Użytkownik'
  const onSettings = isUnder(pathname, '/settings')

  return (
    <div className="app-shell">
      <aside className="app-sidebar">
        <Link to="/" className="sidebar-brand">
          <svg width={24} height={24} viewBox="0 0 24 24" aria-hidden="true">
            <path d="M4 1.5h11L20 6.5v16H4z" fill="currentColor" />
            <path d="M15 1.5v5h5" fill="none" stroke="var(--ui-surface)" strokeWidth={1.6} />
            <circle cx={12} cy={14} r={4} fill="none" stroke="var(--ui-surface)" strokeWidth={2.2} />
          </svg>
          <span>OpenKSeF</span>
        </Link>

        <CompanyCard />

        <nav className="sidebar-nav" data-testid="mobile-nav" aria-label="Nawigacja główna">
          {navItems.map((item) => {
            const active = [item.to, ...(item.alsoOn ?? [])].some((base) => isUnder(pathname, base))
            return (
              <Link
                key={item.to}
                to={item.to}
                className={active ? 'sidebar-nav-link active' : 'sidebar-nav-link'}
                aria-current={active ? 'page' : undefined}
              >
                <Icon name={item.icon} size={18} />
                <span>{item.label}</span>
              </Link>
            )
          })}
        </nav>

        <div className="sidebar-user">
          <span className="sidebar-user__name">{displayName}</span>
          <Link
            to="/settings"
            className={onSettings ? 'sidebar-user__action active' : 'sidebar-user__action'}
            data-testid="nav-settings"
            aria-label="Ustawienia"
            aria-current={onSettings ? 'page' : undefined}
          >
            <Icon name="settings" size={18} />
          </Link>
          <button
            type="button"
            className="sidebar-user__action"
            data-testid="nav-logout"
            aria-label="Wyloguj"
            onClick={() => void logout()}
          >
            <Icon name="log-out" size={18} />
          </button>
        </div>
      </aside>

      <div className="app-content">
        <main className="app-main">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
