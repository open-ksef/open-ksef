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
import { InvoiceApproveReviewPage } from '@/pages/InvoiceApproveReview'
import { InvoiceCorrectionCreatePage } from '@/pages/InvoiceCorrectionCreate'
import { InvoiceDraftCreatePage } from '@/pages/InvoiceDraftCreate'
import { InvoiceFinalFromAdvancesPage } from '@/pages/InvoiceFinalFromAdvances'
import { InvoiceKsefSubmitPage } from '@/pages/InvoiceKsefSubmit'
import { InvoicePrintViewPage } from '@/pages/InvoicePrintView'
import { InvoiceDraftEditPage } from '@/pages/InvoiceDraftEdit'
import { SyncedInvoiceDetailPage } from '@/pages/SyncedInvoiceDetail'
import { InvoiceListPage } from '@/pages/InvoiceList'
import { PurchaseInvoiceListPage } from '@/pages/PurchaseInvoiceList'
import { SalesInvoiceListPage } from '@/pages/SalesInvoiceList'
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
      { path: 'invoices/purchases', element: <PurchaseInvoiceListPage /> },
      { path: 'invoices/sales', element: <SalesInvoiceListPage /> },
      { path: 'invoices/new', element: <InvoiceDraftCreatePage /> },
      { path: 'invoices/final-from-advances', element: <InvoiceFinalFromAdvancesPage /> },
      { path: 'invoices/aggregate/:id', element: <InvoiceAggregateDetailPage /> },
      { path: 'invoices/aggregate/:id/edit', element: <InvoiceDraftEditPage /> },
      { path: 'invoices/aggregate/:id/approve', element: <InvoiceApproveReviewPage /> },
      { path: 'invoices/aggregate/:id/submit', element: <InvoiceKsefSubmitPage /> },
      { path: 'invoices/aggregate/:id/corrections/new', element: <InvoiceCorrectionCreatePage /> },
      { path: 'invoices/aggregate/:id/print', element: <InvoicePrintViewPage /> },
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
