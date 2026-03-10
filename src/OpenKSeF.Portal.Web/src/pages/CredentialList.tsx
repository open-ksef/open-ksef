import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'

import {
  addOrUpdateCredential,
  addOrUpdateCertificateCredential,
  addOrUpdatePemCertificateCredential,
  deleteCredential,
  forceCredentialSync,
  listCredentials,
} from '@/api/endpoints/credentials'
import { listTenants } from '@/api/endpoints/tenants'
import type { CredentialType, TenantCredentialStatusResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { StatusBadge } from '@/components/StatusBadge'
import { Table, type TableColumn } from '@/components/Table'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

type CredentialFormMode = 'add' | 'update' | null

function CredentialTypeBadge({ type }: { type: CredentialType | null }) {
  if (!type) return <span style={{ color: 'var(--ui-text-muted)' }}>-</span>
  return (
    <span
      data-testid="credential-type-badge"
      className={`credential-type-badge credential-type-badge--${type === 'Certificate' ? 'certificate' : 'token'}`}
    >
      {type === 'Certificate' ? 'Certyfikat' : 'Token'}
    </span>
  )
}

export function CredentialListPage(): ReactElement {
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<CredentialFormMode>(null)
  const [selectedTenantId, setSelectedTenantId] = useState('')
  const [formCredentialType, setFormCredentialType] = useState<CredentialType>('Token')
  const [token, setToken] = useState('')
  const [certFormat, setCertFormat] = useState<'pem' | 'pfx'>('pem')
  const [certFile, setCertFile] = useState<File | null>(null)
  const [keyFile, setKeyFile] = useState<File | null>(null)
  const [certPassword, setCertPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pendingDeleteTenantId, setPendingDeleteTenantId] = useState<string | null>(null)
  const certInputRef = useRef<HTMLInputElement>(null)
  const keyInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!mode) {
      return
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMode(null)
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [mode])

  const credentialsQuery = useQuery({
    queryKey: ['credentials'],
    queryFn: () => listCredentials(),
  })

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'credential-form'],
    queryFn: () => listTenants(),
  })

  const addOrUpdateMutation = useMutation({
    mutationFn: async ({ tenantId }: { tenantId: string }) => {
      if (formCredentialType === 'Certificate') {
        if (certFormat === 'pem') {
          if (!certFile || !keyFile) throw new Error('Pliki certyfikatu i klucza są wymagane')
          return addOrUpdatePemCertificateCredential(tenantId, certFile, keyFile, certPassword)
        }
        if (!certFile) throw new Error('Plik certyfikatu jest wymagany')
        return addOrUpdateCertificateCredential(tenantId, certFile, certPassword)
      }
      return addOrUpdateCredential(tenantId, token.trim())
    },
    onSuccess: async () => {
      notifyMutationSuccess('saved', 'Credential')
      await queryClient.invalidateQueries({ queryKey: ['credentials'] })
      setMode(null)
      resetForm()
    },
    onError: (mutationError) => {
      notifyMutationError('save', 'Credential', mutationError)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (tenantId: string) => deleteCredential(tenantId),
    onSuccess: async () => {
      notifyMutationSuccess('deleted', 'Credential')
      await queryClient.invalidateQueries({ queryKey: ['credentials'] })
    },
    onError: (mutationError) => {
      notifyMutationError('delete', 'Credential', mutationError)
    },
  })

  const syncMutation = useMutation({
    mutationFn: (tenantId: string) => forceCredentialSync(tenantId),
    onSuccess: async (result) => {
      toast.success(`Synchronizacja zakończona. Pobrane: ${result.fetchedInvoices}, nowe: ${result.newInvoices}.`)
      await queryClient.invalidateQueries({ queryKey: ['credentials'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      await queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
    onError: (mutationError) => {
      notifyMutationError('save', 'Credential', mutationError)
    },
  })

  const rows = credentialsQuery.data ?? []
  const combinedError = credentialsQuery.error ?? tenantsQuery.error

  const columns: TableColumn<TenantCredentialStatusResponse>[] = [
    { key: 'tenantDisplayName', label: 'Firma' },
    {
      key: 'credentialType',
      label: 'Typ',
      render: (row) => <CredentialTypeBadge type={row.hasCredential ? row.credentialType : null} />,
    },
    {
      key: 'hasCredential',
      label: 'Status',
      render: (row) => {
        if (!row.hasCredential) return <StatusBadge status="warning" label="Brak" />
        return <StatusBadge status="success" label="Skonfigurowany" />
      },
    },
    {
      key: 'lastUpdatedAt',
      label: 'Ostatnia aktualizacja',
      render: (row) =>
        row.lastUpdatedAt ? (
          new Date(row.lastUpdatedAt).toLocaleDateString('pl-PL')
        ) : (
          <span style={{ color: 'var(--ui-text-muted)' }}>-</span>
        ),
    },
    {
      key: 'tenantId',
      label: 'Akcje',
      render: (row) => (
        <div className="table-actions">
          <button
            data-testid="credential-sync-button"
            type="button"
            className="btn-action"
            disabled={!row.hasCredential || syncMutation.isPending}
            onClick={() => {
              setError(null)
              syncMutation.mutate(row.tenantId)
            }}
          >
            Wymuś synchronizację
          </button>
          <button
            data-testid="credential-update-button"
            type="button"
            className="btn-action btn-action--edit"
            onClick={() => {
              setError(null)
              setMode('update')
              setSelectedTenantId(row.tenantId)
              setFormCredentialType(row.credentialType ?? 'Token')
              resetForm()
            }}
          >
            {row.hasCredential ? 'Aktualizuj' : 'Dodaj'}
          </button>
          <button
            data-testid="credential-delete-button"
            type="button"
            className="btn-action btn-action--danger"
            onClick={() => setPendingDeleteTenantId(row.tenantId)}
          >
            Usuń
          </button>
        </div>
      ),
    },
  ]

  const handleDeleteConfirm = useCallback(() => {
    if (pendingDeleteTenantId) {
      deleteMutation.mutate(pendingDeleteTenantId, {
        onSettled: () => setPendingDeleteTenantId(null),
      })
    }
  }, [pendingDeleteTenantId, deleteMutation])

  const handleDeleteCancel = useCallback(() => setPendingDeleteTenantId(null), [])

  const resetForm = () => {
    setToken('')
    setCertFormat('pem')
    setCertFile(null)
    setKeyFile(null)
    setCertPassword('')
  }

  const onSubmit = () => {
    setError(null)
    if (!selectedTenantId) {
      setError('Firma jest wymagana')
      return
    }

    if (formCredentialType === 'Token' && !token.trim()) {
      setError('Token jest wymagany')
      return
    }

    if (formCredentialType === 'Certificate') {
      if (certFormat === 'pem') {
        if (!certFile) {
          setError('Plik certyfikatu (.crt) jest wymagany')
          return
        }
        if (!keyFile) {
          setError('Plik klucza prywatnego (.key) jest wymagany')
          return
        }
      } else {
        if (!certFile) {
          setError('Plik certyfikatu (.pfx/.p12) jest wymagany')
          return
        }
        if (!certPassword) {
          setError('Hasło certyfikatu jest wymagane')
          return
        }
      }
    }

    addOrUpdateMutation.mutate({ tenantId: selectedTenantId })
  }

  const isBusy = addOrUpdateMutation.isPending

  return (
    <section>
      <header className="page-header">
        <h1>Dane logowania</h1>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button
            data-testid="credential-refresh-button"
            type="button"
            onClick={() => {
              void credentialsQuery.refetch()
              void tenantsQuery.refetch()
            }}
          >
            ↺ Odśwież
          </button>
          <Button
            data-testid="credential-add-button"
            onClick={() => {
              setError(null)
              setMode('add')
              setSelectedTenantId(tenantsQuery.data?.[0]?.id ?? '')
              setFormCredentialType('Token')
              resetForm()
            }}
          >
            + Dodaj dane logowania
          </Button>
        </div>
      </header>

      <AsyncStateView
        isLoading={credentialsQuery.isLoading || tenantsQuery.isLoading}
        error={combinedError}
        isEmpty={rows.length === 0}
        emptyTitle="Brak skonfigurowanych danych logowania"
        emptyMessage="Dodaj dane logowania KSeF dla co najmniej jednej firmy, aby włączyć synchronizację faktur."
        onRetry={() => {
          void credentialsQuery.refetch()
          void tenantsQuery.refetch()
        }}
      >
        <Table testId="credential-table" columns={columns} data={rows} />
      </AsyncStateView>

      <ConfirmDialog
        open={pendingDeleteTenantId !== null}
        title="Usunąć dane logowania?"
        message="Dane logowania KSeF dla tej firmy zostaną trwale usunięte. Synchronizacja faktur przestanie działać do momentu dodania nowych danych logowania."
        confirmLabel="Usuń dane logowania"
        variant="danger"
        isPending={deleteMutation.isPending}
        onConfirm={handleDeleteConfirm}
        onCancel={handleDeleteCancel}
      />

      {mode ? (
        <div
          className="ui-modal-backdrop"
          onClick={(e) => {
            if (e.target === e.currentTarget) setMode(null)
          }}
        >
          <div
            className="ui-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="credential-modal-title"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !(e.target instanceof HTMLTextAreaElement)) {
                e.preventDefault()
                onSubmit()
              }
            }}
          >
            <div className="ui-modal-header">
              <h2 className="ui-modal-title" id="credential-modal-title">
                {mode === 'add' ? '+ Dodaj dane logowania' : 'Aktualizuj dane logowania'}
              </h2>
              <button
                type="button"
                className="ui-modal-close"
                aria-label="Zamknij"
                onClick={() => setMode(null)}
              >
                ✕
              </button>
            </div>

            <div className="ui-modal-body">
              <div className="ui-form-group">
                <label htmlFor="credential-tenant-select">Firma</label>
                <select
                  id="credential-tenant-select"
                  data-testid="credential-tenant-select"
                  value={selectedTenantId}
                  disabled={mode === 'update'}
                  onChange={(event) => setSelectedTenantId(event.target.value)}
                >
                  {(tenantsQuery.data ?? []).map((tenant) => (
                    <option key={tenant.id} value={tenant.id}>
                      {tenant.displayName || tenant.nip}
                    </option>
                  ))}
                </select>
              </div>

              <div className="ui-form-group">
                <label>Typ uwierzytelniania</label>
                <div className="onboarding-credential-type" data-testid="credential-type-select">
                  <button
                    type="button"
                    className={`onboarding-credential-type__option${formCredentialType === 'Token' ? ' onboarding-credential-type__option--active' : ''}`}
                    onClick={() => setFormCredentialType('Token')}
                  >
                    Token
                  </button>
                  <button
                    type="button"
                    className={`onboarding-credential-type__option${formCredentialType === 'Certificate' ? ' onboarding-credential-type__option--active' : ''}`}
                    onClick={() => setFormCredentialType('Certificate')}
                  >
                    Certyfikat
                  </button>
                </div>
              </div>

              {formCredentialType === 'Token' && (
                <>
                  <div className="ui-form-group">
                    <label htmlFor="credential-token-input">Token KSeF</label>
                    <textarea
                      id="credential-token-input"
                      data-testid="credential-token-input"
                      value={token}
                      placeholder="Wklej token uwierzytelniający KSeF tutaj..."
                      onInput={(event) => setToken((event.target as HTMLTextAreaElement).value)}
                    />
                    <span className="ui-form-hint">
                      Token zostanie zaszyfrowany przed zapisem. Istniejący token zostanie zastąpiony.
                    </span>
                  </div>
                </>
              )}

              {formCredentialType === 'Certificate' && (
                <>
                  <div className="ui-form-group">
                    <label>Format certyfikatu</label>
                    <div className="onboarding-credential-type" data-testid="credential-cert-format">
                      <button
                        type="button"
                        data-testid="credential-cert-format-pem"
                        className={`onboarding-credential-type__option${certFormat === 'pem' ? ' onboarding-credential-type__option--active' : ''}`}
                        onClick={() => { setCertFormat('pem'); setCertFile(null); setKeyFile(null); setCertPassword('') }}
                      >
                        CRT + KEY
                      </button>
                      <button
                        type="button"
                        data-testid="credential-cert-format-pfx"
                        className={`onboarding-credential-type__option${certFormat === 'pfx' ? ' onboarding-credential-type__option--active' : ''}`}
                        onClick={() => { setCertFormat('pfx'); setCertFile(null); setKeyFile(null); setCertPassword('') }}
                      >
                        PFX / P12
                      </button>
                    </div>
                  </div>

                  {certFormat === 'pem' && (
                    <>
                      <div className="ui-form-group">
                        <label htmlFor="credential-certificate-file">Plik certyfikatu (.crt / .cer / .pem)</label>
                        <input
                          id="credential-certificate-file"
                          data-testid="credential-certificate-file"
                          ref={certInputRef}
                          type="file"
                          accept=".crt,.cer,.pem"
                          onChange={(e) => setCertFile(e.target.files?.[0] ?? null)}
                        />
                        {certFile && (
                          <span className="ui-form-hint">Wybrany plik: {certFile.name}</span>
                        )}
                      </div>
                      <div className="ui-form-group">
                        <label htmlFor="credential-key-file">Klucz prywatny (.key / .pem)</label>
                        <input
                          id="credential-key-file"
                          data-testid="credential-key-file"
                          ref={keyInputRef}
                          type="file"
                          accept=".key,.pem"
                          onChange={(e) => setKeyFile(e.target.files?.[0] ?? null)}
                        />
                        {keyFile && (
                          <span className="ui-form-hint">Wybrany plik: {keyFile.name}</span>
                        )}
                      </div>
                      <div className="ui-form-group">
                        <label htmlFor="credential-certificate-password">
                          Hasło klucza prywatnego{' '}
                          <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(jeśli zaszyfrowany)</span>
                        </label>
                        <input
                          id="credential-certificate-password"
                          data-testid="credential-certificate-password"
                          type="password"
                          placeholder="Hasło do klucza prywatnego"
                          value={certPassword}
                          onInput={(e) => setCertPassword((e.target as HTMLInputElement).value)}
                        />
                      </div>
                    </>
                  )}

                  {certFormat === 'pfx' && (
                    <>
                      <div className="ui-form-group">
                        <label htmlFor="credential-certificate-file">Plik certyfikatu (.pfx / .p12)</label>
                        <input
                          id="credential-certificate-file"
                          data-testid="credential-certificate-file"
                          ref={certInputRef}
                          type="file"
                          accept=".pfx,.p12"
                          onChange={(e) => setCertFile(e.target.files?.[0] ?? null)}
                        />
                        {certFile && (
                          <span className="ui-form-hint">Wybrany plik: {certFile.name}</span>
                        )}
                      </div>
                      <div className="ui-form-group">
                        <label htmlFor="credential-certificate-password">Hasło certyfikatu</label>
                        <input
                          id="credential-certificate-password"
                          data-testid="credential-certificate-password"
                          type="password"
                          placeholder="Hasło do pliku PFX"
                          value={certPassword}
                          onInput={(e) => setCertPassword((e.target as HTMLInputElement).value)}
                        />
                      </div>
                    </>
                  )}
                </>
              )}

              {error ? (
                <div className="ui-form-error" role="alert">
                  Uwaga: {error}
                </div>
              ) : null}
            </div>

            <div className="ui-modal-footer">
              <Button variant="outline" onClick={() => setMode(null)}>
                Anuluj
              </Button>
              <Button data-testid="credential-submit-button" onClick={onSubmit} disabled={isBusy}>
                {isBusy ? 'Zapisywanie...' : 'Zapisz dane logowania'}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  )
}
