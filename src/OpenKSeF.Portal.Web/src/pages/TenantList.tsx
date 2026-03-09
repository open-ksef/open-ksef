import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactElement } from 'react'

import { createTenant, deleteTenant, listTenants, updateTenant } from '@/api/endpoints/tenants'
import type { TenantResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Table, type TableColumn } from '@/components/Table'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

type FormMode = 'create' | 'edit' | null

interface TenantFormState {
  id: string | null
  nip: string
  displayName: string
  notificationEmail: string
}

const INITIAL_FORM: TenantFormState = {
  id: null,
  nip: '',
  displayName: '',
  notificationEmail: '',
}

function validateForm(state: TenantFormState): string | null {
  if (!/^\d{10}$/.test(state.nip)) {
    return 'NIP musi zawierać dokładnie 10 cyfr'
  }

  if (state.notificationEmail && !/^\S+@\S+\.\S+$/.test(state.notificationEmail)) {
    return 'Adres e-mail do powiadomień jest nieprawidłowy'
  }

  return null
}

export function TenantListPage(): ReactElement {
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<FormMode>(null)
  const [form, setForm] = useState<TenantFormState>(INITIAL_FORM)
  const [validationError, setValidationError] = useState<string | null>(null)
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

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

  const tenantsQuery = useQuery({
    queryKey: ['tenants'],
    queryFn: () => listTenants(),
  })

  const createMutation = useMutation({
    mutationFn: createTenant,
    onSuccess: async () => {
      notifyMutationSuccess('created', 'Tenant')
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      setMode(null)
      setForm(INITIAL_FORM)
    },
    onError: (error) => {
      notifyMutationError('create', 'Tenant', error)
    },
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, displayName, notificationEmail }: { id: string; displayName?: string | null; notificationEmail?: string | null }) =>
      updateTenant(id, { displayName, notificationEmail }),
    onSuccess: async () => {
      notifyMutationSuccess('updated', 'Tenant')
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      setMode(null)
      setForm(INITIAL_FORM)
    },
    onError: (error) => {
      notifyMutationError('update', 'Tenant', error)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteTenant(id),
    onSuccess: async () => {
      notifyMutationSuccess('deleted', 'Tenant')
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
    },
    onError: (error) => {
      notifyMutationError('delete', 'Tenant', error)
    },
  })

  const rows = tenantsQuery.data ?? []

  const columns = useMemo<TableColumn<TenantResponse>[]>(() => [
    {
      key: 'nip',
      label: 'NIP',
      render: (row) => <span className="token-display">{row.nip}</span>,
    },
    {
      key: 'displayName',
      label: 'Nazwa wyświetlana',
      render: (row) => row.displayName || <span style={{ color: 'var(--ui-text-muted)' }}>—</span>,
    },
    {
      key: 'notificationEmail',
      label: 'E-mail do powiadomień',
      render: (row) => row.notificationEmail || <span style={{ color: 'var(--ui-text-muted)' }}>—</span>,
    },
    {
      key: 'createdAt',
      label: 'Data dodania',
      render: (row) => new Date(row.createdAt).toLocaleDateString('pl-PL'),
    },
    {
      key: 'id',
      label: 'Akcje',
      render: (row) => (
        <div className="table-actions">
          <button
            data-testid="tenant-edit-button"
            type="button"
            className="btn-action btn-action--edit"
            onClick={() => {
              setValidationError(null)
              setMode('edit')
              setForm({
                id: row.id,
                nip: row.nip,
                displayName: row.displayName ?? '',
                notificationEmail: row.notificationEmail ?? '',
              })
            }}
          >
            ✎ Edytuj
          </button>
          <button
            data-testid="tenant-delete-button"
            type="button"
            className="btn-action btn-action--danger"
            onClick={() => setPendingDeleteId(row.id)}
          >
            ✕ Usuń
          </button>
        </div>
      ),
    },
  ], [])

  const handleDeleteConfirm = useCallback(() => {
    if (pendingDeleteId) {
      deleteMutation.mutate(pendingDeleteId, {
        onSettled: () => setPendingDeleteId(null),
      })
    }
  }, [pendingDeleteId, deleteMutation])

  const handleDeleteCancel = useCallback(() => setPendingDeleteId(null), [])

  const onSubmit = () => {
    setValidationError(null)
    const error = validateForm(form)
    if (error) {
      setValidationError(error)
      return
    }

    if (mode === 'create') {
      createMutation.mutate({
        nip: form.nip,
        displayName: form.displayName || null,
        notificationEmail: form.notificationEmail || null,
      })
      return
    }

    if (mode === 'edit' && form.id) {
      updateMutation.mutate({
        id: form.id,
        displayName: form.displayName || null,
        notificationEmail: form.notificationEmail || null,
      })
    }
  }

  const isBusy = createMutation.isPending || updateMutation.isPending

  return (
    <section>
      <header className="page-header">
        <h1>Firmy</h1>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button data-testid="tenant-refresh-button" type="button" onClick={() => void tenantsQuery.refetch()}>
            ↺ Odśwież
          </button>
          <Button
            data-testid="tenant-create-button"
            onClick={() => {
              setValidationError(null)
              setMode('create')
              setForm(INITIAL_FORM)
            }}
          >
            + Nowa firma
          </Button>
        </div>
      </header>

      <AsyncStateView
        isLoading={tenantsQuery.isLoading}
        error={tenantsQuery.error}
        isEmpty={rows.length === 0}
        emptyTitle="Brak firm"
        emptyMessage="Dodaj pierwszą firmę, aby rozpocząć synchronizację faktur."
        onRetry={() => void tenantsQuery.refetch()}
      >
        <Table testId="tenant-table" columns={columns} data={rows} />
      </AsyncStateView>

      <ConfirmDialog
        open={pendingDeleteId !== null}
        title="Usunąć firmę?"
        message="Ta operacja jest nieodwracalna. Wszystkie powiązane dane logowania i synchronizacje zostaną usunięte."
        confirmLabel="Usuń firmę"
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
            aria-labelledby="tenant-modal-title"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !(e.target instanceof HTMLTextAreaElement)) {
                e.preventDefault()
                onSubmit()
              }
            }}
          >
            <div className="ui-modal-header">
              <h2 className="ui-modal-title" id="tenant-modal-title">
                {mode === 'create' ? '+ Nowa firma' : '✎ Edytuj firmę'}
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
                <label htmlFor="tenant-form-nip">
                  NIP
                </label>
                <input
                  id="tenant-form-nip"
                  data-testid="tenant-form-nip"
                  type="text"
                  inputMode="numeric"
                  placeholder="1234567890"
                  value={form.nip}
                  disabled={mode === 'edit'}
                  onInput={(event) => setForm((current) => ({ ...current, nip: (event.target as HTMLInputElement).value }))}
                />
                <span className="ui-form-hint">10-cyfrowy numer identyfikacji podatkowej</span>
              </div>

              <div className="ui-form-group">
                <label htmlFor="tenant-form-display-name">
                  Nazwa wyświetlana{' '}
                  <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span>
                </label>
                <input
                  id="tenant-form-display-name"
                  data-testid="tenant-form-display-name"
                  type="text"
                  placeholder="np. Acme Sp. z o.o."
                  value={form.displayName}
                  onInput={(event) =>
                    setForm((current) => ({ ...current, displayName: (event.target as HTMLInputElement).value }))
                  }
                />
              </div>

              <div className="ui-form-group">
                <label htmlFor="tenant-form-notification-email">
                  E-mail do powiadomień{' '}
                  <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span>
                </label>
                <input
                  id="tenant-form-notification-email"
                  data-testid="tenant-form-notification-email"
                  type="email"
                  placeholder="invoices@company.pl"
                  value={form.notificationEmail}
                  onInput={(event) =>
                    setForm((current) => ({ ...current, notificationEmail: (event.target as HTMLInputElement).value }))
                  }
                />
              </div>

              {validationError ? (
                <div className="ui-form-error" role="alert">
                  ⚠ {validationError}
                </div>
              ) : null}
            </div>

            <div className="ui-modal-footer">
              <Button variant="outline" onClick={() => setMode(null)}>
                Anuluj
              </Button>
              <Button data-testid="tenant-form-submit" onClick={onSubmit} disabled={isBusy}>
                {isBusy ? 'Zapisywanie…' : 'Zapisz firmę'}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  )
}
