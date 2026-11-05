import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { Link, Outlet, useLocation, useMatch } from 'react-router-dom'

import type { InvoiceResponse } from '@/api/types'
import { useAuth } from '@/auth/useAuth'
import { CompanyCard } from '@/company/CompanyCard'
import { useCompany } from '@/company/useCompany'
import { BrandMark } from './BrandMark'
import { Icon, type IconName } from './Icon'

const navItems: { to: string; label: string; icon: IconName }[] = [
  { to: '/', label: 'Start', icon: 'house' },
  { to: '/invoices', label: 'Otrzymane', icon: 'inbox' },
  { to: '/issued', label: 'Wystawione', icon: 'send' },
  { to: '/company', label: 'Firma', icon: 'building-2' },
  { to: '/mobile-setup', label: 'Telefon', icon: 'smartphone' },
]

const isUnder = (pathname: string, base: string): boolean =>
  base === '/' ? pathname === '/' : pathname === base || pathname.startsWith(`${base}/`)

export function Layout(): ReactElement {
  const { user, logout } = useAuth()
  const { pathname, state } = useLocation()
  const { tenantId } = useCompany()
  const number = useMatch('/invoices/:ksefInvoiceNumber')?.params.ksefInvoiceNumber
  const { data } = useQuery<InvoiceResponse>({ queryKey: ['invoice-details', tenantId, number], enabled: false })
  const current = number && (data?.direction === 'Issued' || (state as { fromIssued?: boolean } | null)?.fromIssued) ? '/issued' : pathname

  const displayName = user?.profile?.name ?? user?.profile?.email ?? 'Użytkownik'
  const onSettings = isUnder(pathname, '/settings')

  return (
    <div className="app-shell">
      <aside className="app-sidebar">
        <Link to="/" className="sidebar-brand">
          <BrandMark size={24} />
          <span>OpenKSeF</span>
        </Link>

        <CompanyCard />

        <nav className="sidebar-nav" data-testid="mobile-nav" aria-label="Nawigacja główna">
          {navItems.map((item) => {
            const active = isUnder(current, item.to)
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
          <span className="sidebar-user__name" title={displayName}>{displayName}</span>
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
