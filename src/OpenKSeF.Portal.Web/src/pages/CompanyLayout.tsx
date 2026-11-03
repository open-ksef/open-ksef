import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { NavLink, Outlet } from 'react-router-dom'

import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { companyName } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'
import { EnvBadge } from '@/components/invoicing/KsefStatusBadge'
import '@/components/invoicing/invoicing.css'

const tabs = [
  { to: '/company/invoice-data', label: 'Dane do faktur' },
  { to: '/company/token', label: 'Token KSeF i pobieranie' },
  { to: '/company/details', label: 'Dane firmy' },
]

export function CompanyLayoutPage(): ReactElement {
  const { tenantId, company } = useCompany()
  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })

  return (
    <section className="company-page">
      <header className="page-header company-head">
        <div>
          <h1>Firma</h1>
          {company ? (
            <p className="company-head__sub">
              {companyName(company)} · <span className="mono">NIP {company.nip}</span>
            </p>
          ) : null}
        </div>
        {profileQuery.data ? <EnvBadge environment={profileQuery.data.ksefEnvironment} /> : null}
      </header>
      <nav className="ui-tabs" aria-label="Sekcje firmy">
        {tabs.map((tab) => (
          <NavLink key={tab.to} to={tab.to}>
            {tab.label}
          </NavLink>
        ))}
      </nav>
      <Outlet />
    </section>
  )
}
