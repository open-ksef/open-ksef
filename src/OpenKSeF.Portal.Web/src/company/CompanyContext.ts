import { createContext } from 'react'

import type { KsefEnvironment, TenantResponse, TenantRole } from '@/api/types'

export interface CompanySwitchGuard {
  dirty: boolean
  busy: boolean
  environment: KsefEnvironment | null
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

export const can = (company: TenantResponse | null, permission: string): boolean => company?.permissions.includes(permission) ?? false

export const MANAGERS_ONLY = 'Zmienić to może tylko właściciel lub administrator firmy.'

export const ROLE_LABELS: Record<TenantRole, string> = {
  Owner: 'Właściciel',
  Administrator: 'Administrator',
  Accountant: 'Księgowy',
  Employee: 'Pracownik',
}
