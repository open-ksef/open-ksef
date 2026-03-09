import type { RouteObject } from 'react-router-dom'
import { createBrowserRouter, Link } from 'react-router-dom'

import { Layout } from '@/components/Layout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { Callback } from '@/pages/Callback'
import { CredentialListPage } from '@/pages/CredentialList'
import { DashboardPage } from '@/pages/Dashboard'
import { DeviceListPage } from '@/pages/DeviceList'
import { InvoiceDetailsPage } from '@/pages/InvoiceDetails'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { LoginPage } from '@/pages/Login'
import { OnboardingPage } from '@/pages/Onboarding'
import { SilentCallbackPage } from '@/pages/SilentCallback'
import { MobileSetupPage } from '@/pages/MobileSetup'
import { TenantListPage } from '@/pages/TenantList'
import { AdminSetupPage } from '@/pages/AdminSetup'

function NotFoundPage() {
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

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: (
      <ProtectedRoute>
        <Layout />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'invoices', element: <InvoiceListPage /> },
      { path: 'invoices/:ksefInvoiceNumber', element: <InvoiceDetailsPage /> },
      { path: 'tenants', element: <TenantListPage /> },
      { path: 'credentials', element: <CredentialListPage /> },
      { path: 'devices', element: <DeviceListPage /> },
      { path: 'mobile-setup', element: <MobileSetupPage /> },
    ],
  },
  {
    path: '/onboarding',
    element: (
      <ProtectedRoute>
        <OnboardingPage />
      </ProtectedRoute>
    ),
  },
  { path: '/callback', element: <Callback /> },
  { path: '/silent-callback', element: <SilentCallbackPage /> },
  { path: '/login', element: <LoginPage /> },
  { path: '/admin-setup', element: <AdminSetupPage /> },
  { path: '*', element: <NotFoundPage /> },
]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
