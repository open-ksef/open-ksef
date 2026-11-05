import type { RouteObject } from 'react-router-dom'
import { createBrowserRouter } from 'react-router-dom'

import { CompanyProvider } from '@/company/CompanyProvider'
import { Layout } from '@/components/Layout'
import { Moved } from '@/components/Moved'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { AdminSetupPage } from '@/pages/AdminSetup'
import { Callback } from '@/pages/Callback'
import { CompanyDetailsPage } from '@/pages/CompanyDetails'
import { CompanyLayoutPage } from '@/pages/CompanyLayout'
import { CompanyTokenPage } from '@/pages/CompanyToken'
import { InvoiceDetailsPage } from '@/pages/InvoiceDetails'
import { InvoiceDraftPage } from '@/pages/InvoiceDraft'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { IssuedInvoiceDetailsPage } from '@/pages/IssuedInvoiceDetails'
import { IssuedInvoiceListPage } from '@/pages/IssuedInvoiceList'
import { LoginPage } from '@/pages/Login'
import { MobileSetupPage } from '@/pages/MobileSetup'
import { NotFoundPage } from '@/pages/NotFound'
import { SellerProfilePage } from '@/pages/SellerProfile'
import { SettingsPage } from '@/pages/Settings'
import { SettingsAdminPage } from '@/pages/SettingsAdmin'
import { OnboardingPage } from '@/pages/Onboarding'
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
      { path: 'invoices/:ksefInvoiceNumber', element: <InvoiceDetailsPage /> },
      { path: 'issued', element: <IssuedInvoiceListPage /> },
      { path: 'issued/new', element: <InvoiceDraftPage /> },
      { path: 'issued/:id', element: <IssuedInvoiceDetailsPage /> },
      { path: 'issued/:id/edit', element: <InvoiceDraftPage /> },
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
      { path: 'mobile-setup', element: <MobileSetupPage /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: 'settings/admin', element: <SettingsAdminPage /> },
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
