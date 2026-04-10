import type { RouteObject } from 'react-router-dom'
import { createBrowserRouter } from 'react-router-dom'

import { Layout } from '@/components/Layout'
import { ProtectedRoute } from '@/components/ProtectedRoute'
import { AdminSetupPage } from '@/pages/AdminSetup'
import { Callback } from '@/pages/Callback'
import { CredentialListPage } from '@/pages/CredentialList'
import { DashboardPage } from '@/pages/Dashboard'
import { DeviceListPage } from '@/pages/DeviceList'
import { InvoiceAggregateDetailPage } from '@/pages/InvoiceAggregateDetail'
import { InvoiceDraftCreatePage } from '@/pages/InvoiceDraftCreate'
import { InvoiceDraftEditPage } from '@/pages/InvoiceDraftEdit'
import { SyncedInvoiceDetailPage } from '@/pages/SyncedInvoiceDetail'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { LoginPage } from '@/pages/Login'
import { MobileSetupPage } from '@/pages/MobileSetup'
import { NotFoundPage } from '@/pages/NotFound'
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
      { path: 'invoices/new', element: <InvoiceDraftCreatePage /> },
      { path: 'invoices/final-from-advances', element: <div /> },
      { path: 'invoices/aggregate/:id', element: <InvoiceAggregateDetailPage /> },
      { path: 'invoices/aggregate/:id/edit', element: <InvoiceDraftEditPage /> },
      { path: 'invoices/aggregate/:id/approve', element: <div /> },
      { path: 'invoices/aggregate/:id/submit', element: <div /> },
      { path: 'invoices/aggregate/:id/corrections/new', element: <div /> },
      { path: 'invoices/aggregate/:id/print', element: <div /> },
      { path: 'invoices/:ksefInvoiceNumber', element: <SyncedInvoiceDetailPage /> },
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
