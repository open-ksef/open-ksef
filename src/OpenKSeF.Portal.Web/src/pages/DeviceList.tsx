import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactElement } from 'react'
import { useNavigate } from 'react-router-dom'

import { listTenants } from '@/api/endpoints/tenants'
import { listDevices, registerDevice, sendTestNotification, unregisterDevice } from '@/api/endpoints/devices'
import type { DeviceTokenResponse } from '@/api/types'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Table, type TableColumn } from '@/components/Table'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

type DeviceFormMode = 'register' | null

const PLATFORM_LABELS: Record<number, { name: string; icon: string; variant: string }> = {
  0: { name: 'Android', icon: '🤖', variant: 'android' },
  1: { name: 'iOS', icon: '🍎', variant: 'ios' },
}

function getPlatformBadge(platform: number): ReactElement {
  const info = PLATFORM_LABELS[platform] ?? { name: `Platform ${platform}`, icon: '🌐', variant: 'web' }

  return (
    <span className={`platform-badge platform-badge--${info.variant}`}>
      {info.icon} {info.name}
    </span>
  )
}

export function DeviceListPage(): ReactElement {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<DeviceFormMode>(null)
  const [token, setToken] = useState('')
  const [platform, setPlatform] = useState(0)
  const [tenantId, setTenantId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pendingUnregisterToken, setPendingUnregisterToken] = useState<string | null>(null)
  const [testingToken, setTestingToken] = useState<string | null>(null)

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

  const devicesQuery = useQuery({
    queryKey: ['devices'],
    queryFn: () => listDevices(),
  })

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'device-form'],
    queryFn: () => listTenants(),
  })

  const registerMutation = useMutation({
    mutationFn: registerDevice,
    onSuccess: async () => {
      notifyMutationSuccess('registered', 'Device')
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
      setMode(null)
      setToken('')
    },
    onError: (error) => {
      notifyMutationError('register', 'Device', error)
    },
  })

  const unregisterMutation = useMutation({
    mutationFn: unregisterDevice,
    onSuccess: async () => {
      notifyMutationSuccess('unregistered', 'Device')
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
    },
    onError: (error) => {
      notifyMutationError('unregister', 'Device', error)
    },
  })

  const testMutation = useMutation({
    mutationFn: sendTestNotification,
    onSuccess: (data) => {
      if (data.success) {
        notifyMutationSuccess('sent', 'Test notification')
      } else {
        notifyMutationError('send', 'Test notification', new Error(data.error ?? 'Push delivery failed'))
      }
    },
    onError: (err) => {
      notifyMutationError('send', 'Test notification', err)
    },
    onSettled: () => setTestingToken(null),
  })

  const rows = devicesQuery.data ?? []
  const combinedError = devicesQuery.error ?? tenantsQuery.error
  const tenantLookup = useMemo(() => {
    const map = new Map<string, string>()
    for (const tenant of tenantsQuery.data ?? []) {
      map.set(tenant.id, tenant.displayName || tenant.nip)
    }
    return map
  }, [tenantsQuery.data])

  const columns = useMemo<TableColumn<DeviceTokenResponse>[]>(
    () => [
      {
        key: 'token',
        label: 'Token',
        render: (row) => (
          <span className="token-display">
            {row.token.slice(0, 8)}…{row.token.slice(-4)}
          </span>
        ),
      },
      {
        key: 'platform',
        label: 'Platforma',
        render: (row) => getPlatformBadge(row.platform),
      },
      {
        key: 'tenantId',
        label: 'Firma',
        render: (row) =>
          row.tenantId ? (
            tenantLookup.get(row.tenantId) || row.tenantId
          ) : (
            <span className="ui-status-badge ui-status-badge--success" style={{ fontSize: '11px' }}>
              Wszystkie firmy
            </span>
          ),
      },
      {
        key: 'createdAt',
        label: 'Zarejestrowano',
        render: (row) => new Date(row.createdAt).toLocaleDateString('pl-PL'),
      },
      {
        key: 'updatedAt',
        label: 'Zaktualizowano',
        render: (row) => new Date(row.updatedAt).toLocaleDateString('pl-PL'),
      },
      {
        key: 'id',
        label: 'Akcje',
        render: (row) => (
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              data-testid="device-test-button"
              type="button"
              className="btn-action"
              disabled={testingToken === row.token}
              onClick={() => {
                setTestingToken(row.token)
                testMutation.mutate(row.token)
              }}
            >
              {testingToken === row.token ? 'Wysyłanie…' : 'Testuj'}
            </button>
            <button
              data-testid="device-unregister-button"
              type="button"
              className="btn-action btn-action--danger"
              onClick={() => setPendingUnregisterToken(row.token)}
            >
              ✕ Wyrejestruj
            </button>
          </div>
        ),
      },
    ],
    [tenantLookup, testingToken, testMutation],
  )

  const handleUnregisterConfirm = useCallback(() => {
    if (pendingUnregisterToken) {
      unregisterMutation.mutate(pendingUnregisterToken, {
        onSettled: () => setPendingUnregisterToken(null),
      })
    }
  }, [pendingUnregisterToken, unregisterMutation])

  const handleUnregisterCancel = useCallback(() => setPendingUnregisterToken(null), [])

  const onSubmit = () => {
    setError(null)
    if (!token.trim()) {
      setError('Token jest wymagany')
      return
    }

    registerMutation.mutate({
      token: token.trim(),
      platform,
      tenantId: tenantId || null,
    })
  }

  const isBusy = registerMutation.isPending

  return (
    <section>
      <header className="page-header">
        <h1>Urządzenia</h1>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button
            data-testid="device-refresh-button"
            type="button"
            onClick={() => {
              void devicesQuery.refetch()
              void tenantsQuery.refetch()
            }}
          >
            ↺ Odśwież
          </button>
          <Button
            data-testid="device-connect-mobile-button"
            onClick={() => void navigate('/mobile-setup')}
          >
            + Połącz aplikację mobilną
          </Button>
        </div>
      </header>

      <AsyncStateView
        isLoading={devicesQuery.isLoading || tenantsQuery.isLoading}
        error={combinedError}
        isEmpty={rows.length === 0}
        emptyTitle="Brak zarejestrowanych urządzeń"
        emptyMessage="Połącz aplikację mobilną za pomocą kodu QR, aby automatycznie skonfigurować urządzenie i otrzymywać powiadomienia push."
        onRetry={() => {
          void devicesQuery.refetch()
          void tenantsQuery.refetch()
        }}
      >
        <Table testId="device-table" columns={columns} data={rows} />
      </AsyncStateView>

      <div style={{ marginTop: '16px', textAlign: 'right' }}>
        <button
          data-testid="device-register-button"
          type="button"
          className="btn-action"
          style={{ fontSize: '12px', opacity: 0.7 }}
          onClick={() => {
            setMode('register')
            setError(null)
            setToken('')
            setPlatform(0)
            setTenantId('')
          }}
        >
          Zaawansowane: ręczna rejestracja tokenu
        </button>
      </div>

      <ConfirmDialog
        open={pendingUnregisterToken !== null}
        title="Wyrejestrować urządzenie?"
        message="Urządzenie przestanie otrzymywać powiadomienia push o synchronizacji faktur."
        confirmLabel="Wyrejestruj"
        variant="danger"
        isPending={unregisterMutation.isPending}
        onConfirm={handleUnregisterConfirm}
        onCancel={handleUnregisterCancel}
      />

      {mode === 'register' ? (
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
            aria-labelledby="device-modal-title"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !(e.target instanceof HTMLTextAreaElement)) {
                e.preventDefault()
                onSubmit()
              }
            }}
          >
            <div className="ui-modal-header">
              <h2 className="ui-modal-title" id="device-modal-title">
                + Zarejestruj urządzenie
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
                <label htmlFor="device-form-token">Token push</label>
                <input
                  id="device-form-token"
                  data-testid="device-form-token"
                  type="text"
                  placeholder="Wklej token push urządzenia…"
                  value={token}
                  onInput={(event) => setToken((event.target as HTMLInputElement).value)}
                />
                <span className="ui-form-hint">
                  Token powiadomień push z iOS/Android/Web.
                </span>
              </div>

              <div className="ui-form-group">
                <label htmlFor="device-form-platform">Platforma</label>
                <select
                  id="device-form-platform"
                  data-testid="device-form-platform"
                  value={platform}
                  onChange={(event) => setPlatform(Number(event.target.value))}
                >
                  <option value={0}>🤖 Android</option>
                  <option value={1}>🍎 iOS</option>
                </select>
              </div>

              <div className="ui-form-group">
                <label htmlFor="device-form-tenant">
                  Firma{' '}
                  <span style={{ fontWeight: 400, color: 'var(--ui-text-muted)' }}>(opcjonalnie)</span>
                </label>
                <select
                  id="device-form-tenant"
                  data-testid="device-form-tenant"
                  value={tenantId}
                  onChange={(event) => setTenantId(event.target.value)}
                >
                  <option value="">Wszystkie firmy</option>
                  {(tenantsQuery.data ?? []).map((tenant) => (
                    <option key={tenant.id} value={tenant.id}>
                      {tenant.displayName || tenant.nip}
                    </option>
                  ))}
                </select>
                <span className="ui-form-hint">
                  Pozostaw „Wszystkie firmy", aby otrzymywać powiadomienia dla każdej firmy.
                </span>
              </div>

              {error ? (
                <div className="ui-form-error" role="alert">
                  ⚠ {error}
                </div>
              ) : null}
            </div>

            <div className="ui-modal-footer">
              <Button variant="outline" onClick={() => setMode(null)}>
                Anuluj
              </Button>
              <Button data-testid="device-form-submit" onClick={onSubmit} disabled={isBusy}>
                {isBusy ? 'Rejestrowanie…' : 'Zarejestruj urządzenie'}
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  )
}
