import type { RouteObject } from 'react-router-dom'
import { createBrowserRouter } from 'react-router-dom'

import { Layout } from '@/components/Layout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { AdminSetupPage } from '@/pages/AdminSetup'
import { Callback } from '@/pages/Callback'
import { CredentialListPage } from '@/pages/CredentialList'
import { DashboardPage } from '@/pages/Dashboard'
import { DeviceListPage } from '@/pages/DeviceList'
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
import { OnboardingPage } from '@/pages/Onboarding'
import { SilentCallbackPage } from '@/pages/SilentCallback'
import { TenantListPage } from '@/pages/TenantList'

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
      { path: 'issued', element: <IssuedInvoiceListPage /> },
      { path: 'issued/new', element: <InvoiceDraftPage /> },
      { path: 'issued/:id', element: <IssuedInvoiceDetailsPage /> },
      { path: 'issued/:id/edit', element: <InvoiceDraftPage /> },
      { path: 'seller-profile', element: <SellerProfilePage /> },
      { path: 'tenants', element: <TenantListPage /> },
      { path: 'credentials', element: <CredentialListPage /> },
      { path: 'devices', element: <DeviceListPage /> },
      { path: 'mobile-setup', element: <MobileSetupPage /> },
      { path: 'settings', element: <SettingsPage /> },
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
