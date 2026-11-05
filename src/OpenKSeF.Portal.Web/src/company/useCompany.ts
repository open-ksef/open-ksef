import { useContext } from 'react'

import { CompanyContext, type CompanyContextValue } from './CompanyContext'

export function useCompany(): CompanyContextValue {
  const context = useContext(CompanyContext)
  if (!context) {
    throw new Error('useCompany must be used within CompanyProvider')
  }

  return context
}
