import type { RouteObject } from 'react-router-dom'
import { Outlet, createBrowserRouter } from 'react-router-dom'

import { CompanyProvider } from '@/company/CompanyProvider'
import { Layout } from '@/components/Layout'
import { Moved } from '@/components/Moved'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { RouteError } from '@/components/RouteError'
import { loadRoute } from '@/loadRoute'
import { Callback } from '@/pages/Callback'
import { CompanyDetailsPage } from '@/pages/CompanyDetails'
import { CompanyLayoutPage } from '@/pages/CompanyLayout'
import { CompanyTokenPage } from '@/pages/CompanyToken'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { IssuedInvoiceListPage } from '@/pages/IssuedInvoiceList'
import { LoginPage } from '@/pages/Login'
import { NotFoundPage } from '@/pages/NotFound'
import { SellerProfilePage } from '@/pages/SellerProfile'
import { StartPage } from '@/pages/Start'

export const appRoutes: RouteObject[] = [
  {
    hydrateFallbackElement: <main>Ładowanie…</main>,
    errorElement: <RouteError />,
    children: [
      {
        path: '/',
        element: (
          <ProtectedRoute>
            <CompanyProvider>
              <Layout />
            </CompanyProvider>
          </ProtectedRoute>
        ),
        children: [
          { index: true, element: <StartPage /> },
          { path: 'invoices', element: <InvoiceListPage /> },
          { path: 'invoices/:ksefInvoiceNumber', lazy: () => loadRoute(() => import('@/pages/InvoiceDetails')).then((m) => ({ Component: m.InvoiceDetailsPage })) },
          { path: 'issued', element: <IssuedInvoiceListPage /> },
          { path: 'issued/new', lazy: () => loadRoute(() => import('@/pages/InvoiceDraft')).then((m) => ({ Component: m.InvoiceDraftPage })) },
          { path: 'issued/:id', lazy: () => loadRoute(() => import('@/pages/IssuedInvoiceDetails')).then((m) => ({ Component: m.IssuedInvoiceDetailsPage })) },
          { path: 'issued/:id/edit', lazy: () => loadRoute(() => import('@/pages/InvoiceDraft')).then((m) => ({ Component: m.InvoiceDraftPage })) },
          {
            path: 'company',
            element: <CompanyLayoutPage />,
            children: [
              { index: true, element: <Moved to="/company/invoice-data" /> },
              { path: 'invoice-data', element: <SellerProfilePage /> },
              { path: 'token', element: <CompanyTokenPage /> },
              { path: 'details', element: <CompanyDetailsPage /> },
            ],
          },
          { path: 'seller-profile', element: <Moved to="/company/invoice-data" /> },
          { path: 'tenants', element: <Moved to="/company/details" /> },
          { path: 'credentials', element: <Moved to="/company/token" /> },
          { path: 'devices', element: <Moved to="/mobile-setup" /> },
          { path: 'mobile-setup', lazy: () => loadRoute(() => import('@/pages/MobileSetup')).then((m) => ({ Component: m.MobileSetupPage })) },
          { path: 'settings', lazy: () => loadRoute(() => import('@/pages/Settings')).then((m) => ({ Component: m.SettingsPage })) },
          { path: 'settings/security', lazy: () => loadRoute(() => import('@/pages/SettingsSecurity')).then((m) => ({ Component: m.SettingsSecurityPage })) },
          { path: 'settings/admin', lazy: () => loadRoute(() => import('@/pages/SettingsAdmin')).then((m) => ({ Component: m.SettingsAdminPage })) },
        ],
      },
      {
        element: (
          <ProtectedRoute>
            <Outlet />
          </ProtectedRoute>
        ),
        children: [{ path: '/onboarding', lazy: () => loadRoute(() => import('@/pages/Onboarding')).then((m) => ({ Component: m.OnboardingPage })) }],
      },
      { path: '/callback', element: <Callback /> },
      { path: '/login', element: <LoginPage /> },
      { path: '/admin-setup', lazy: () => loadRoute(() => import('@/pages/AdminSetup')).then((m) => ({ Component: m.AdminSetupPage })) },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
