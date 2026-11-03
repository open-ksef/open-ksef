import { useCallback, useEffect, useRef, useState, type PropsWithChildren, type ReactElement } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import toast from 'react-hot-toast'

import type { KsefEnvironment, TenantResponse } from '@/api/types'
import { useTenants } from '@/hooks/useTenants'
import { CompanyContext, companyName, type CompanySwitchGuard } from './CompanyContext'
import { CompanySwitchDialog } from './CompanySwitchDialog'

const STORAGE_KEY = 'openksef.company'

function readStored(): string {
  try {
    return localStorage.getItem(STORAGE_KEY) ?? ''
  } catch {
    return ''
  }
}

function writeStored(id: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, id)
  } catch {
    return
  }
}

function pathAfterSwitch(pathname: string): string {
  if (/^\/invoices\/[^/]+$/.test(pathname)) return '/invoices'
  if (/^\/issued\/[^/]+\/edit$/.test(pathname)) return '/issued/new'
  if (pathname !== '/issued/new' && /^\/issued\/[^/]+$/.test(pathname)) return '/issued'
  return pathname
}

interface PendingDialog {
  target: TenantResponse
  environment: KsefEnvironment
  fromKey: string
}

export function CompanyProvider({ children }: PropsWithChildren): ReactElement {
  const { data: tenants } = useTenants()
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const guardRef = useRef<CompanySwitchGuard | null>(null)

  const [chosenId, setChosenId] = useState(readStored)
  const [seenUrlId, setSeenUrlId] = useState('')
  const [arriving, setArriving] = useState<{ id: string; fromKey: string } | null>(null)
  const [dialog, setDialog] = useState<PendingDialog | null>(null)

  const urlId = searchParams.get('tenantId') ?? ''
  const validUrlId = tenants?.some((tenant) => tenant.id === urlId) ? urlId : ''

  if (validUrlId !== seenUrlId) {
    setSeenUrlId(validUrlId)
    if (validUrlId) setChosenId(validUrlId)
  }

  // A switch applies with the navigation, not before: a render of the new company on the old URL would query it with the old page or invoice id.
  if (arriving && location.key !== arriving.fromKey) {
    setArriving(null)
    setChosenId(arriving.id)
  }

  useEffect(() => {
    if (validUrlId) writeStored(validUrlId)
  }, [validUrlId])

  const company =
    tenants?.find((tenant) => tenant.id === validUrlId) ??
    tenants?.find((tenant) => tenant.id === chosenId) ??
    tenants?.[0] ??
    null

  const setSwitchGuard = useCallback((guard: CompanySwitchGuard | null) => {
    guardRef.current = guard
  }, [])

  function applySwitch(target: TenantResponse, notice: string): void {
    writeStored(target.id)
    setArriving({ id: target.id, fromKey: location.key })
    setDialog(null)

    const params = new URLSearchParams(location.search)
    params.delete('tenantId')
    params.delete('page')
    const pathname = pathAfterSwitch(location.pathname)
    void navigate(
      { pathname, search: params.toString() },
      pathname === '/issued/new' ? { state: { companyNotice: notice } } : undefined,
    )
  }

  function switchCompany(id: string): void {
    const target = tenants?.find((tenant) => tenant.id === id)
    if (!target || target.id === company?.id) return

    const guard = guardRef.current
    if (guard?.busy) {
      toast.error('Trwa zapisywanie lub wysyłanie faktury. Zmień firmę, gdy się zakończy.')
      return
    }
    if (guard?.dirty) {
      setDialog({ target, environment: guard.environment, fromKey: location.key })
      return
    }
    applySwitch(target, `Nowy sprzedawca: ${companyName(target)}`)
  }

  return (
    <CompanyContext.Provider
      value={{ tenantId: company?.id ?? '', company, tenants: tenants ?? [], switchCompany, setSwitchGuard }}
    >
      {children}
      {dialog && company && dialog.fromKey === location.key ? (
        <CompanySwitchDialog
          current={company}
          target={dialog.target}
          environment={dialog.environment}
          onStay={() => setDialog(null)}
          onClear={() => applySwitch(dialog.target, `Formularz wyczyszczony. Nowy sprzedawca: ${companyName(dialog.target)}`)}
          onSave={async () => {
            await guardRef.current?.save()
            applySwitch(
              dialog.target,
              `Szkic zapisany w firmie ${companyName(company)}. Nowy sprzedawca: ${companyName(dialog.target)}`,
            )
          }}
        />
      ) : null}
    </CompanyContext.Provider>
  )
}
