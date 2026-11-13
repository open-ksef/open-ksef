import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { useState, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate } from 'react-router-dom'

import { listDevices, sendTestNotification, unregisterDevice } from '@/api/endpoints/devices'
import { listTenants } from '@/api/endpoints/tenants'
import type { DeviceTokenResponse } from '@/api/types'
import { signOutAllDevices } from '@/auth/signOutAll'
import { AsyncStateView } from '@/components/AsyncStateView'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { Table, type TableColumn } from '@/components/Table'
import { formatWarsawDate } from '@/invoicing/format'
import { notifyMutationError, notifyMutationSuccess } from '@/notifications'

const PLATFORMS: Record<number, string> = { 0: 'Android', 1: 'iOS' }

function buildQrPayload(serverUrl: string): string {
  return JSON.stringify({
    type: 'openksef-setup',
    version: 1,
    serverUrl,
  })
}

const deviceName = (device: DeviceTokenResponse): string => `Telefon: ${PLATFORMS[device.platform] ?? 'inna platforma'}`
const shortToken = (token: string): string => `${token.slice(0, 8)}…${token.slice(-4)}`

export function MobileSetupPage(): ReactElement {
  const serverUrl = globalThis.location?.origin ?? 'http://localhost:8080'
  const queryClient = useQueryClient()
  const [pendingRemoval, setPendingRemoval] = useState<DeviceTokenResponse | null>(null)
  const [testingId, setTestingId] = useState<string | null>(null)
  const [confirmSignOutAll, setConfirmSignOutAll] = useState(false)
  const navigate = useNavigate()

  const devicesQuery = useQuery({ queryKey: ['devices'], queryFn: () => listDevices() })
  const tenantsQuery = useQuery({ queryKey: ['tenants'], queryFn: () => listTenants() })

  const remove = useMutation({
    mutationFn: unregisterDevice,
    onSuccess: async () => {
      notifyMutationSuccess('deleted', 'Device')
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
    },
    onError: (error) => notifyMutationError('delete', 'Device', error),
    onSettled: () => setPendingRemoval(null),
  })

  const signOutAll = useMutation({
    mutationFn: signOutAllDevices,
    onSuccess: () => navigate('/login', { replace: true }),
    onError: async (error) => {
      toast.error(`Nie udało się wylogować wszystkich urządzeń: ${error.message}`)
      setConfirmSignOutAll(false)
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
    },
  })

  const sendTest = useMutation({
    mutationFn: sendTestNotification,
    onSuccess: (data) => {
      if (data.success) toast.success('Wysłano powiadomienie testowe.')
      else toast.error(`Nie udało się wysłać powiadomienia testowego: ${data.error ?? 'brak potwierdzenia dostarczenia'}`)
    },
    onError: (error) => toast.error(`Nie udało się wysłać powiadomienia testowego: ${error.message}`),
    onSettled: () => setTestingId(null),
  })

  const companyOf = (device: DeviceTokenResponse): string => {
    if (!device.tenantId) return 'Wszystkie firmy'
    const tenant = tenantsQuery.data?.find((row) => row.id === device.tenantId)
    return tenant ? tenant.displayName || tenant.nip : '—'
  }

  const columns: TableColumn<DeviceTokenResponse>[] = [
    {
      key: 'platform',
      label: 'Urządzenie',
      render: (row) => (
        <span title={`Identyfikator: ${shortToken(row.token)}`}>
          <b className="device-name">{deviceName(row)}</b>
          <span className="device-dates">
            Dodany {formatWarsawDate(row.createdAt)}, aktualizacja {formatWarsawDate(row.updatedAt)}
          </span>
        </span>
      ),
    },
    { key: 'tenantId', label: 'Powiadomienia', className: 'device-company', render: companyOf },
    {
      key: 'id',
      label: 'Akcje',
      className: 'device-actions',
      render: (row) => (
        <div className="device-actions__row">
          <Button
            variant="outline"
            size="sm"
            data-testid="device-test-button"
            disabled={testingId === row.id}
            onClick={() => {
              setTestingId(row.id)
              sendTest.mutate(row.id)
            }}
          >
            {testingId === row.id ? 'Wysyłanie…' : 'Wyślij test'}
          </Button>
          <button
            type="button"
            className="ui-button ui-button--danger-outline ui-button--sm"
            data-testid="device-unregister-button"
            aria-label={`Usuń urządzenie: ${PLATFORMS[row.platform] ?? 'inna platforma'}, dodane ${formatWarsawDate(row.createdAt)}`}
            onClick={() => setPendingRemoval(row)}
          >
            Usuń
          </button>
        </div>
      ),
    },
  ]

  return (
    <section>
      <header className="page-header">
        <h1>Telefon</h1>
      </header>

      <div className="company-grid">
        <section className="ui-card" aria-labelledby="pair-title">
          <h2 id="pair-title">Połącz aplikację mobilną</h2>
          <div className="phone-pair">
            <div className="phone-pair__qr" data-testid="mobile-setup-qr">
              <QRCodeSVG value={buildQrPayload(serverUrl)} size={220} level="M" marginSize={4} title="Kod QR z adresem serwera" />
            </div>
            <div>
              <p>Zeskanuj ten kod QR w aplikacji OpenKSeF, aby ustawić adres serwera. Potem zaloguj się w aplikacji.</p>
              <p className="phone-pair__server">
                <span className="inv-caption">Adres serwera</span>
                <br />
                <span className="mono">{serverUrl}</span>
              </p>
              <h3>Instrukcja</h3>
              <ol className="phone-pair__howto">
                <li>Zainstaluj aplikację OpenKSeF na telefonie.</li>
                <li>Na ekranie logowania w aplikacji dotknij przycisku QR.</li>
                <li>Zeskanuj ten kod. Adres serwera zostanie ustawiony, a aplikacja wróci do ekranu logowania.</li>
              </ol>
            </div>
          </div>
        </section>

        <section className="ui-card" aria-labelledby="devices-title">
          <h2 id="devices-title">Moje urządzenia</h2>
          <AsyncStateView
            isLoading={devicesQuery.isLoading}
            error={devicesQuery.error}
            isEmpty={(devicesQuery.data ?? []).length === 0}
            emptyTitle="Brak zarejestrowanych urządzeń"
            emptyMessage="Zeskanuj kod QR w aplikacji mobilnej, aby ustawić adres serwera. Po zalogowaniu w aplikacji telefon będzie mógł otrzymywać powiadomienia."
            onRetry={() => void devicesQuery.refetch()}
          >
            <Table testId="device-table" columns={columns} data={devicesQuery.data ?? []} />
          </AsyncStateView>
          <p className="inv-help">Powiadomienia o nowych fakturach przychodzą na urządzenia z tej listy.</p>
          <button
            type="button"
            className="ui-button ui-button--danger-outline ui-button--md"
            data-testid="sign-out-all-button"
            onClick={() => setConfirmSignOutAll(true)}
          >
            Wyloguj wszystkie urządzenia
          </button>
        </section>
      </div>

      <ConfirmDialog
        open={pendingRemoval !== null}
        title="Usunąć urządzenie?"
        message={
          pendingRemoval
            ? `${deviceName(pendingRemoval)}, dodany ${formatWarsawDate(pendingRemoval.createdAt)}, przestanie otrzymywać powiadomienia o nowych fakturach.`
            : ''
        }
        confirmLabel="Usuń urządzenie"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => pendingRemoval && remove.mutate(pendingRemoval.id)}
        onCancel={() => setPendingRemoval(null)}
      />

      <ConfirmDialog
        open={confirmSignOutAll}
        title="Wylogować wszystkie urządzenia?"
        message="Aplikacja mobilna zostanie wylogowana na każdym telefonie, a telefony przestaną dostawać powiadomienia. Portal zostanie wylogowany we wszystkich przeglądarkach, także w tej. Token dostępu wydany wcześniej może działać jeszcze kilka minut."
        confirmLabel="Wyloguj wszystkie"
        pendingLabel="Wylogowywanie…"
        variant="danger"
        isPending={signOutAll.isPending}
        onConfirm={() => signOutAll.mutate()}
        onCancel={() => setConfirmSignOutAll(false)}
      />
    </section>
  )
}
