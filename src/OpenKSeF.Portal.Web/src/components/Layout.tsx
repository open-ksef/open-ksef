import { useState, type ReactElement } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'

const topNavItems = [
  { to: '/', label: 'Pulpit', icon: '⊞', end: true },
]

const invoiceChildren = [
  { to: '/invoices/purchases', label: 'Zakupy' },
  { to: '/invoices/sales', label: 'Sprzedaż' },
]

const bottomNavItems = [
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
  const location = useLocation()

  const isOnInvoices = location.pathname.startsWith('/invoices')
  const [isInvoicesOpen, setIsInvoicesOpen] = useState(isOnInvoices)

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
          {topNavItems.map((item) => (
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

          <div className="sidebar-nav-group">
            <button
              type="button"
              data-testid="invoices-nav-toggle"
              className={`sidebar-nav-group__toggle${isOnInvoices ? ' active' : ''}`}
              onClick={() => setIsInvoicesOpen((prev) => !prev)}
            >
              <span className="nav-icon" aria-hidden="true">≡</span>
              <span>Faktury</span>
              <span className={`sidebar-nav-group__chevron${isInvoicesOpen ? ' sidebar-nav-group__chevron--open' : ''}`}>
                ▾
              </span>
            </button>

            <div
              className={`sidebar-nav-group__children${isInvoicesOpen ? ' sidebar-nav-group__children--open' : ''}`}
              data-testid="invoices-nav-children"
            >
              {invoiceChildren.map((child) => (
                <NavLink
                  key={child.to}
                  to={child.to}
                  className="sidebar-nav-group__child"
                  onClick={() => setIsMobileNavOpen(false)}
                >
                  {child.label}
                </NavLink>
              ))}
            </div>
          </div>

          {bottomNavItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
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
