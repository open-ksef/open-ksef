import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState, type ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { getSellerProfile } from '@/api/endpoints/sellerProfile'
import { Icon } from '@/components/Icon'
import { EnvBadge } from '@/components/invoicing/KsefStatusBadge'
import { companyName } from './CompanyContext'
import { useCompany } from './useCompany'

export function CompanyCard(): ReactElement {
  const { tenantId, company, tenants, switchCompany } = useCompany()
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const rootRef = useRef<HTMLDetailsElement>(null)

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: open && Boolean(tenantId),
  })
  const environment = profileQuery.data?.ksefEnvironment

  useEffect(() => {
    if (!open) return
    const closeMenu = () => {
      setOpen(false)
      setSearch('')
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) closeMenu()
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      closeMenu()
      rootRef.current?.querySelector('summary')?.focus()
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  if (!company) {
    return (
      <div className="company-card" data-testid="company-card">
        <span className="company-card__caption">Firma</span>
        <b className="company-card__name">—</b>
      </div>
    )
  }

  const needle = search.trim().toLowerCase()
  const options = tenants.filter((tenant) => `${companyName(tenant)} ${tenant.nip}`.toLowerCase().includes(needle))

  return (
    <details ref={rootRef} className="company-switcher" open={open}>
      <summary
        className="company-card"
        data-testid="company-card"
        aria-label={`Firma: ${companyName(company)}, NIP ${company.nip}. Zmień firmę`}
        onClick={(event) => {
          event.preventDefault()
          setOpen(!open)
          setSearch('')
        }}
      >
        <span className="company-card__caption">
          Firma <Icon name="chevron-down" size={14} />
        </span>
        <b className="company-card__name" title={companyName(company)}>
          {companyName(company)}
        </b>
        <span className="company-nip">NIP {company.nip}</span>
      </summary>
      <div className="company-menu">
        <label htmlFor="company-search" className="visually-hidden">
          Szukaj firmy po nazwie lub NIP
        </label>
        <input
          id="company-search"
          type="search"
          data-testid="company-search"
          placeholder="Szukaj: nazwa lub NIP"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        {options.map((tenant) => (
          <button
            key={tenant.id}
            type="button"
            className="company-option"
            data-testid={`company-option-${tenant.id}`}
            aria-pressed={tenant.id === tenantId}
            onClick={() => {
              setOpen(false)
              setSearch('')
              switchCompany(tenant.id)
            }}
          >
            <b>{companyName(tenant)}</b>
            <span className="company-option__meta">
              <span className="company-nip">NIP {tenant.nip}</span>
              {environment ? <EnvBadge environment={environment} /> : null}
            </span>
          </button>
        ))}
        <Link to="/company/details?add=1" className="company-add" data-testid="company-add" onClick={() => setOpen(false)}>
          <Icon name="plus" /> Dodaj firmę
        </Link>
      </div>
    </details>
  )
}
