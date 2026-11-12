import type { RouteObject } from 'react-router-dom'
import { Outlet, createBrowserRouter } from 'react-router-dom'

import { CompanyProvider } from '@/company/CompanyProvider'
import { Layout } from '@/components/Layout'
import { Moved } from '@/components/Moved'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { Callback } from '@/pages/Callback'
import { CompanyDetailsPage } from '@/pages/CompanyDetails'
import { CompanyLayoutPage } from '@/pages/CompanyLayout'
import { CompanyTokenPage } from '@/pages/CompanyToken'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { IssuedInvoiceListPage } from '@/pages/IssuedInvoiceList'
import { LoginPage } from '@/pages/Login'
import { NotFoundPage } from '@/pages/NotFound'
import { SellerProfilePage } from '@/pages/SellerProfile'
import { SilentCallbackPage } from '@/pages/SilentCallback'
import { StartPage } from '@/pages/Start'

export const appRoutes: RouteObject[] = [
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
      { path: 'invoices/:ksefInvoiceNumber', lazy: async () => ({ Component: (await import('@/pages/InvoiceDetails')).InvoiceDetailsPage }) },
      { path: 'issued', element: <IssuedInvoiceListPage /> },
      { path: 'issued/new', lazy: async () => ({ Component: (await import('@/pages/InvoiceDraft')).InvoiceDraftPage }) },
      { path: 'issued/:id', lazy: async () => ({ Component: (await import('@/pages/IssuedInvoiceDetails')).IssuedInvoiceDetailsPage }) },
      { path: 'issued/:id/edit', lazy: async () => ({ Component: (await import('@/pages/InvoiceDraft')).InvoiceDraftPage }) },
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
      { path: 'mobile-setup', lazy: async () => ({ Component: (await import('@/pages/MobileSetup')).MobileSetupPage }) },
      { path: 'settings', lazy: async () => ({ Component: (await import('@/pages/Settings')).SettingsPage }) },
      { path: 'settings/admin', lazy: async () => ({ Component: (await import('@/pages/SettingsAdmin')).SettingsAdminPage }) },
    ],
  },
  {
    element: (
      <ProtectedRoute>
        <Outlet />
      </ProtectedRoute>
    ),
    children: [{ path: '/onboarding', lazy: async () => ({ Component: (await import('@/pages/Onboarding')).OnboardingPage }) }],
  },
  { path: '/callback', element: <Callback /> },
  { path: '/silent-callback', element: <SilentCallbackPage /> },
  { path: '/login', element: <LoginPage /> },
  { path: '/admin-setup', lazy: async () => ({ Component: (await import('@/pages/AdminSetup')).AdminSetupPage }) },
  { path: '*', element: <NotFoundPage /> },
]

export function createAppRouter() {
  return createBrowserRouter(appRoutes)
}
