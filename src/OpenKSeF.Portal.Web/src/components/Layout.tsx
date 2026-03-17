import { useState, type ReactElement } from 'react'
import { NavLink, Outlet } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'

const navItems = [
  { to: '/', label: 'Pulpit', icon: '⊞', end: true },
  { to: '/invoices', label: 'Faktury', icon: '≡' },
  { to: '/tenants', label: 'Firmy', icon: '◉' },
  { to: '/credentials', label: 'Dane logowania', icon: '⚿' },
  { to: '/devices', label: 'Urządzenia', icon: '◈' },
  { to: '/mobile-setup', label: 'Aplikacja mobilna', icon: '⬡' },
  { to: '/settings', label: 'Ustawienia', icon: '⚙' },
]

function getInitials(name: string | undefined | null): string {
  if (!name) return 'U'
  const parts = name.trim().split(/\s+/)
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase()
  return name.slice(0, 2).toUpperCase()
}

export function Layout(): ReactElement {
  const { user, logout } = useAuth()
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false)

  const displayName = user?.profile?.name ?? user?.profile?.email ?? 'Użytkownik'
  const initials = getInitials(user?.profile?.name ?? user?.profile?.email)

  return (
    <div className="app-shell">
      <aside className="app-sidebar" data-open={isMobileNavOpen}>
        <div className="sidebar-brand">
          <span className="sidebar-logo-icon" aria-hidden="true">⚡</span>
          <span className="sidebar-logo-text">OpenKSeF</span>
        </div>

        <nav
          className="sidebar-nav"
          data-testid="mobile-nav"
          data-open={isMobileNavOpen}
          aria-label="Nawigacja główna"
        >
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className="sidebar-nav-link"
              onClick={() => setIsMobileNavOpen(false)}
            >
              <span className="nav-icon" aria-hidden="true">{item.icon}</span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="user-avatar" aria-hidden="true">{initials}</div>
          <span className="user-name">{displayName}</span>
          <button
            type="button"
            className="sidebar-logout"
            aria-label="Wyloguj"
            onClick={() => void logout()}
          >
            ↪
          </button>
        </div>
      </aside>

      <div className="app-content">
        <button
          type="button"
          className="mobile-nav-toggle"
          data-testid="mobile-nav-toggle"
          aria-expanded={isMobileNavOpen}
          aria-label={isMobileNavOpen ? 'Zamknij menu' : 'Otwórz menu'}
          onClick={() => setIsMobileNavOpen((current) => !current)}
        >
          {isMobileNavOpen ? '✕' : '☰'}
        </button>

        <main className="app-main">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
