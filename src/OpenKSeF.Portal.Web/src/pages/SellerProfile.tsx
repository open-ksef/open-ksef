import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type FormEvent, type ReactElement, type Ref } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'

import { getSellerProfile, updateSellerProfile } from '@/api/endpoints/sellerProfile'
import { readValidationEnvelope } from '@/api/errors'
import type { NipLookupResponse, SellerProfileResponse } from '@/api/types'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Skeleton } from '@/components/Skeleton'
import { EnvBadge } from '@/components/invoicing/KsefStatusBadge'
import { NipLookupField } from '@/components/invoicing/NipLookupField'
import '@/components/invoicing/invoicing.css'
import { useTenants } from '@/hooks/useTenants'
import { formatNrb } from '@/invoicing/format'
import { toFieldErrors } from '@/invoicing/form'
import {
  NUMBER_TOKENS,
  digitsOnly,
  fromProfile,
  previewNumber,
  toSellerProfileRequest,
  validateSellerProfile,
  type SellerProfileFormValue,
} from '@/invoicing/sellerProfile'

export function SellerProfilePage(): ReactElement {
  const [searchParams] = useSearchParams()
  const tenantsQuery = useTenants()
  const tenantId = searchParams.get('tenantId') || tenantsQuery.data?.[0]?.id || ''

  const profileQuery = useQuery({
    queryKey: ['seller-profile', tenantId],
    queryFn: () => getSellerProfile(tenantId),
    enabled: Boolean(tenantId),
  })
  const error = tenantsQuery.error ?? profileQuery.error

  return (
    <div className="invoicing">
      {error ? (
        <ErrorBanner
          message={error instanceof Error ? error.message : 'Nie udało się załadować danych'}
          onRetry={() => void profileQuery.refetch()}
        />
      ) : !profileQuery.data ? (
        <Skeleton lines={8} />
      ) : (
        <SellerProfileForm
          key={tenantId}
          tenantId={tenantId}
          profile={profileQuery.data}
          continueToDraft={searchParams.get('next') === 'new'}
        />
      )}
    </div>
  )
}

interface SellerProfileFormProps {
  tenantId: string
  profile: SellerProfileResponse
  continueToDraft: boolean
}

function SellerProfileForm({ tenantId, profile, continueToDraft }: SellerProfileFormProps): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const patternRef = useRef<HTMLInputElement>(null)
  const [form, setForm] = useState<SellerProfileFormValue>(() => fromProfile(profile))
  const [accounts, setAccounts] = useState<string[]>([])
  const [otherAccount, setOtherAccount] = useState(true)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [failure, setFailure] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const set = (patch: Partial<SellerProfileFormValue>): void => {
    setForm((current) => ({ ...current, ...patch }))
    setSaved(false)
  }

  const save = useMutation({
    mutationFn: () => updateSellerProfile(tenantId, toSellerProfileRequest(form, profile, new Date())),
    onSuccess: (result) => {
      setErrors({})
      setFailure(null)
      setSaved(true)
      queryClient.setQueryData(['seller-profile', tenantId], result)
      if (continueToDraft && result.isComplete) {
        navigate(`/issued/new?tenantId=${encodeURIComponent(tenantId)}`)
      }
    },
    onError: async (saveError) => {
      const envelope = await readValidationEnvelope(saveError)
      setErrors(envelope ? toFieldErrors(envelope.errors) : {})
      setFailure(envelope ? null : saveError instanceof Error ? saveError.message : 'Nie udało się zapisać danych')
    },
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    const clientErrors = validateSellerProfile(form)
    setErrors(clientErrors)
    if (Object.keys(clientErrors).length === 0) save.mutate()
  }

  function onFound(result: NipLookupResponse): void {
    const current = digitsOnly(form.bankAccount)
    const keepCurrent = current !== '' && !result.bankAccounts.includes(current)
    set({
      legalName: result.name,
      addressLine1: result.addressLine1 ?? form.addressLine1,
      addressLine2: result.addressLine2 ?? form.addressLine2,
      bankAccount: keepCurrent || result.bankAccounts.length === 0 ? form.bankAccount : current || result.bankAccounts[0],
    })
    setAccounts(result.bankAccounts)
    setOtherAccount(keepCurrent || result.bankAccounts.length === 0)
  }

  function insertToken(token: string): void {
    const input = patternRef.current
    const start = input?.selectionStart ?? form.numberingPattern.length
    const end = input?.selectionEnd ?? start
    set({ numberingPattern: form.numberingPattern.slice(0, start) + token + form.numberingPattern.slice(end) })
    requestAnimationFrame(() => {
      input?.focus()
      input?.setSelectionRange(start + token.length, start + token.length)
    })
  }

  const unchangedNumbering =
    form.numberingPattern === profile.numberingPattern && form.nextNumber === String(profile.nextNumber)
  const preview = unchangedNumbering
    ? profile.nextNumberPreview
    : previewNumber(form.numberingPattern, Number(form.nextNumber) || 1, new Date())

  return (
    <form onSubmit={onSubmit} noValidate className="inv-stack" aria-labelledby="seller-profile-title">
      <header className="inv-header">
        <h1 id="seller-profile-title">Dane do faktur</h1>
        <EnvBadge environment={profile.ksefEnvironment} />
      </header>

      {!profile.isComplete ? (
        <div className="inv-alert inv-alert--info" role="status">
          Uzupełnij dane firmy, aby wystawiać faktury.
        </div>
      ) : null}
      {failure ? <ErrorBanner message={failure} /> : null}

      <div className="inv-card-grid">
        <section className="inv-card" aria-labelledby="seller-title">
          <h2 id="seller-title">Sprzedawca</h2>
          <NipLookupField id="seller-nip" label="NIP" value={profile.nip} readOnly onChange={() => {}} onFound={onFound} />
          <TextField id="legal-name" label="Nazwa firmy" value={form.legalName} error={errors.legalName} required onChange={(legalName) => set({ legalName })} />
          <TextField id="address-line-1" label="Adres (ulica i numer)" value={form.addressLine1} error={errors.addressLine1} required onChange={(addressLine1) => set({ addressLine1 })} />
          <TextField id="address-line-2" label="Kod pocztowy i miejscowość" value={form.addressLine2} error={errors.addressLine2} onChange={(addressLine2) => set({ addressLine2 })} />
          <label className="inv-check">
            <input type="checkbox" checked={form.cashAccounting} onChange={(event) => set({ cashAccounting: event.target.checked })} />
            <span>
              Metoda kasowa
              <span className="inv-help">
                Dodaje adnotację „metoda kasowa” (P_16)
              </span>
            </span>
          </label>
        </section>

        <div className="inv-stack">
          <fieldset className="inv-card" aria-describedby="bank-help">
            <legend>Rachunek bankowy</legend>
            {accounts.map((account) => (
              <label key={account} className="inv-check">
                <input
                  type="radio"
                  name="bank-account"
                  checked={!otherAccount && digitsOnly(form.bankAccount) === account}
                  onChange={() => {
                    setOtherAccount(false)
                    set({ bankAccount: account })
                  }}
                />
                <span className="mono">{formatNrb(account)}</span>
              </label>
            ))}
            {accounts.length > 0 ? (
              <label className="inv-check">
                <input
                  type="radio"
                  name="bank-account"
                  checked={otherAccount}
                  onChange={() => {
                    setOtherAccount(true)
                    set({ bankAccount: '' })
                  }}
                />
                Inny rachunek
              </label>
            ) : null}
            {otherAccount ? (
              <TextField
                id="bank-account"
                label="Numer rachunku (NRB)"
                value={form.bankAccount}
                error={errors.bankAccount}
                mono
                onChange={(bankAccount) => set({ bankAccount })}
              />
            ) : errors.bankAccount ? (
              <span className="inv-field-error">{errors.bankAccount}</span>
            ) : null}
            <p className="inv-help" id="bank-help">
              Rachunek spoza białej listy to ryzyko podatkowe nabywcy przy płatnościach powyżej 15 000 zł.
            </p>
          </fieldset>

          <section className="inv-card" aria-labelledby="numbering-title">
            <h2 id="numbering-title">Numeracja</h2>
            <div className="inv-grid">
              <TextField
                id="numbering-pattern"
                label="Wzorzec numeru"
                value={form.numberingPattern}
                error={errors.numberingPattern}
                inputRef={patternRef}
                mono
                onChange={(numberingPattern) => set({ numberingPattern })}
              />
              <TextField
                id="next-number"
                label="Następny numer"
                value={form.nextNumber}
                error={errors.nextNumber ?? errors.nextNumberPeriod}
                numeric
                onChange={(nextNumber) => set({ nextNumber: digitsOnly(nextNumber) })}
              />
            </div>
            <div className="inv-chips" role="group" aria-label="Wstaw znacznik do wzorca">
              {NUMBER_TOKENS.map((token) => (
                <button key={token} type="button" className="mono" onClick={() => insertToken(token)}>
                  {token}
                </button>
              ))}
            </div>
            <p className="inv-help" role="status">
              Następna faktura otrzyma numer <span className="mono">{preview}</span>
            </p>
          </section>
        </div>
      </div>

      <footer className="inv-header">
        <span className="inv-help" role="status">
          {saved ? 'Zapisano dane do faktur.' : ''}
        </span>
        <div className="inv-header__actions">
          <Link to={`/issued?tenantId=${encodeURIComponent(tenantId)}`} className="ui-button ui-button--outline ui-button--md">
            Anuluj
          </Link>
          <button type="submit" className="ui-button ui-button--primary ui-button--md" disabled={save.isPending}>
            {save.isPending ? 'Zapisywanie…' : 'Zapisz dane'}
          </button>
        </div>
      </footer>
    </form>
  )
}

interface TextFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  error?: string
  required?: boolean
  mono?: boolean
  numeric?: boolean
  inputRef?: Ref<HTMLInputElement>
}

function TextField({ id, label, value, onChange, error, required, mono, numeric, inputRef }: TextFieldProps): ReactElement {
  return (
    <div className="ui-form-group">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        ref={inputRef}
        type="text"
        className={mono ? 'mono' : undefined}
        inputMode={numeric ? 'numeric' : undefined}
        value={value}
        aria-required={required}
        aria-invalid={error ? 'true' : 'false'}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
      />
      {error ? (
        <span className="inv-field-error" id={`${id}-error`}>
          {error}
        </span>
      ) : null}
    </div>
  )
}
