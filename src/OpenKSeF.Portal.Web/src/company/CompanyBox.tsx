import type { ReactElement } from 'react'

import type { KsefEnvironment, TenantResponse } from '@/api/types'
import { EnvBadge } from '@/components/invoicing/KsefStatusBadge'
import { companyName } from './CompanyContext'

interface CompanyBoxProps {
  caption: string
  company: TenantResponse
  environment?: KsefEnvironment
}

export function CompanyBox({ caption, company, environment }: CompanyBoxProps): ReactElement {
  return (
    <div className="company-switch-seller">
      <span className="company-switch-seller__caption">{caption}</span>
      <b>{companyName(company)}</b>
      <span className="company-nip">NIP {company.nip}</span> {environment ? <EnvBadge environment={environment} /> : null}
    </div>
  )
}
