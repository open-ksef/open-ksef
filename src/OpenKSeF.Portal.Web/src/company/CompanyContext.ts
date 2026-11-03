import { createContext } from 'react'

import type { KsefEnvironment, TenantResponse } from '@/api/types'

export interface CompanySwitchGuard {
  dirty: boolean
  busy: boolean
  environment: KsefEnvironment
  save: () => Promise<unknown>
}

export interface CompanyContextValue {
  tenantId: string
  company: TenantResponse | null
  tenants: TenantResponse[]
  switchCompany: (id: string) => void
  setSwitchGuard: (guard: CompanySwitchGuard | null) => void
}

export const CompanyContext = createContext<CompanyContextValue | undefined>(undefined)

export const companyName = (company: TenantResponse): string => company.displayName || company.nip
