// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { RouterProvider, createMemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getSetupStatus } from '@/api/endpoints/system'
import {
  changeMemberRole,
  createInvite,
  listInvites,
  listMembers,
  listTenants,
  removeMember,
  resendInvite,
  revokeInvite,
  setMyNotifications,
} from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import type { TenantResponse } from '@/api/types'
import { useAuth } from '@/auth/useAuth'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { employeeAccess, ownerAccess } from '@/test/tenant'
import { CompanyMembersPage } from './CompanyMembers'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
  listMembers: vi.fn(),
  changeMemberRole: vi.fn(),
  removeMember: vi.fn(),
  setMyNotifications: vi.fn(),
  listInvites: vi.fn(),
  createInvite: vi.fn(),
  resendInvite: vi.fn(),
  revokeInvite: vi.fn(),
}))
vi.mock('@/api/endpoints/system', () => ({ getSetupStatus: vi.fn() }))
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const tenant = (access: Pick<TenantResponse, 'role' | 'permissions' | 'notificationsEnabled'>): TenantResponse => ({
  id: 'a',
  nip: '1111111111',
  displayName: 'Firma A',
  notificationEmail: null,
  createdAt: '2026-01-01T00:00:00Z',
  ...access,
})
const adminAccess = { ...ownerAccess, role: 'Administrator' as const, permissions: ownerAccess.permissions.filter((p) => p !== 'tenant.delete') }
const member = (userId: string, role: TenantResponse['role'], displayName: string | null = userId, email: string | null = `${userId}@x.pl`) => ({
  userId,
  displayName,
  email,
  role,
  createdAt: '2026-01-01T00:00:00Z',
})
const invite = { id: 'inv-1', email: 'e@x.pl', role: 'Employee' as const, invitedByName: 'Anna', createdAt: '2026-11-01T00:00:00Z', expiresAt: '2026-11-15T00:00:00Z', isExpired: false }

function Location(): ReactElement {
  return <output data-testid="location">{useLocation().pathname}</output>
}

describe('Firma, Osoby z dostępem', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const all = (id: string) => [...container.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`)]
  const options = (select: HTMLElement | null) => [...(select?.querySelectorAll('option') ?? [])].map((option) => option.textContent)

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 8; tick++) await act(flush)
  }

  async function render(access?: Pick<TenantResponse, 'role' | 'permissions' | 'notificationsEnabled'>): Promise<void> {
    if (access) vi.mocked(listTenants).mockResolvedValue([tenant(access)])
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '*',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <Location />
                <CompanyMembersPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/company/members?tenantId=a'] },
    )
    await act(async () => root.render(<RouterProvider router={router} />))
    await settle()
  }

  async function type(id: string, value: string): Promise<void> {
    const input = by(id) as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  async function choose(select: HTMLElement, value: string): Promise<void> {
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set?.call(select, value)
      select.dispatchEvent(new Event('change', { bubbles: true }))
    })
    await settle()
  }

  async function click(element: HTMLElement | null | undefined): Promise<void> {
    await act(async () => element?.click())
    await settle()
  }

  const confirm = () => click(container.querySelector<HTMLElement>('[role="alertdialog"] .ui-button--danger'))

  async function inviteSomeone(email: string, role?: string): Promise<void> {
    await click(by('invite-open'))
    await type('invite-email', email)
    if (role) await choose(by('invite-role')!, role)
    await click(by('invite-submit'))
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(useAuth).mockReturnValue({ user: { profile: { sub: 'me', email: 'me@x.pl' } }, logout: vi.fn() } as unknown as ReturnType<typeof useAuth>)
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, registrationAllowed: true })
    vi.mocked(listMembers).mockResolvedValue([member('me', 'Owner'), member('b', 'Accountant', 'Basia')])
    vi.mocked(listInvites).mockResolvedValue([invite])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('lists members with roles; an owner can pick every role and remove others, not self', async () => {
    await render(ownerAccess)

    expect(by('my-role')?.textContent).toBe('Twoja rola: Właściciel')
    const rows = all('member-row')
    expect(rows.map((row) => row.querySelector('b')?.textContent)).toEqual(['me (Ty)', 'Basia'])
    expect(options(rows[1].querySelector('select'))).toEqual(['Właściciel', 'Administrator', 'Księgowy', 'Pracownik'])
    expect(rows[0].querySelector('[data-testid="member-remove"]')).toBeNull()

    await choose(rows[1].querySelector('select')!, 'Administrator')
    expect(changeMemberRole).toHaveBeenCalledExactlyOnceWith('a', 'b', 'Administrator')

    await click(rows[1].querySelector<HTMLElement>('[data-testid="member-remove"]'))
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('Basia straci dostęp do tej firmy.')
    await confirm()
    expect(removeMember).toHaveBeenCalledExactlyOnceWith('a', 'b')
  })

  it('the remove dialog says the company notification e-mail stops, only for that member', async () => {
    vi.mocked(listMembers).mockResolvedValue([member('me', 'Owner'), { ...member('b', 'Accountant', 'Basia'), receivesCompanyNotificationEmail: true }, member('c', 'Employee')])
    await render({ ...ownerAccess })
    const [, basia, other] = all('member-row')

    await click(basia.querySelector<HTMLElement>('[data-testid="member-remove"]'))
    expect(by('remove-notification-email')?.textContent).toBe('Adres b@x.pl przestanie dostawać powiadomienia e-mail o nowych fakturach tej firmy.')
    await click(container.querySelector<HTMLElement>('[role="alertdialog"] .ui-button--outline'))

    await click(other.querySelector<HTMLElement>('[data-testid="member-remove"]'))
    expect(by('remove-notification-email')).toBeNull()
  })

  it('the leave dialog says your address stops getting company e-mails', async () => {
    vi.mocked(listMembers).mockResolvedValue([{ ...member('me', 'Owner'), receivesCompanyNotificationEmail: true }, member('o2', 'Owner')])
    await render(ownerAccess)
    await click(by('leave-company-button'))
    expect(by('leave-notification-email')?.textContent).toBe('Twój adres me@x.pl przestanie dostawać powiadomienia e-mail o nowych fakturach tej firmy.')
  })

  it('a member without the list matches own e-mail to the company address, case-insensitive', async () => {
    vi.mocked(listTenants).mockResolvedValue([{ ...tenant(employeeAccess), notificationEmail: ' ME@x.pl ' }])
    await render()
    await click(by('leave-company-button'))
    expect(by('leave-notification-email')).not.toBeNull()
  })

  it('no e-mail sentence when the company address is someone else', async () => {
    vi.mocked(listTenants).mockResolvedValue([{ ...tenant(employeeAccess), notificationEmail: 'biuro@x.pl' }])
    await render()
    await click(by('leave-company-button'))
    expect(by('leave-notification-email')).toBeNull()
  })

  it('changing your own role asks first; cancel keeps it', async () => {
    vi.mocked(changeMemberRole).mockResolvedValue(undefined)
    vi.mocked(listMembers).mockResolvedValue([member('me', 'Owner'), member('o2', 'Owner')])
    await render(ownerAccess)
    const mine = () => all('member-row')[0].querySelector('select')!

    await choose(mine(), 'Employee')
    expect(changeMemberRole).not.toHaveBeenCalled()
    const dialog = () => container.querySelector('[role="alertdialog"]')
    expect(dialog()?.textContent).toContain('Zmienić swoją rolę?')
    expect(dialog()?.textContent).toContain('Nowa rola: Pracownik. Możesz stracić możliwość zarządzania firmą.')
    await click(dialog()?.querySelector<HTMLElement>('.ui-button--outline'))
    expect(dialog()).toBeNull()
    expect((mine() as HTMLSelectElement).value).toBe('Owner')

    await choose(mine(), 'Employee')
    await confirm()
    expect(changeMemberRole).toHaveBeenCalledExactlyOnceWith('a', 'me', 'Employee')
  })

  it('an administrator cannot pick, change or remove an owner', async () => {
    vi.mocked(listMembers).mockResolvedValue([member('o', 'Owner'), member('me', 'Administrator'), member('e', 'Employee')])
    await render(adminAccess)

    const [owner, , employee] = all('member-row')
    expect(owner.querySelector('select')).toBeNull()
    expect(owner.querySelector('[data-testid="member-role-label"]')?.textContent).toBe('Właściciel')
    expect(owner.querySelector('[data-testid="member-remove"]')).toBeNull()
    expect(options(employee.querySelector('select'))).toEqual(['Administrator', 'Księgowy', 'Pracownik'])

    await click(by('invite-open'))
    expect(options(by('invite-role'))).toEqual(['Administrator', 'Księgowy', 'Pracownik'])
    expect((by('invite-role') as HTMLSelectElement).value).toBe('Employee')
  })

  it('a member without members.manage sees only own access: no list, no invites, no invite button', async () => {
    await render(employeeAccess)

    expect(by('my-role')?.textContent).toBe('Twoja rola: Pracownik')
    expect(listMembers).not.toHaveBeenCalled()
    expect(listInvites).not.toHaveBeenCalled()
    expect(by('invite-open')).toBeNull()
    expect(by('members-list')).toBeNull()
    expect(container.querySelector('#invites-title')).toBeNull()
    expect(by('leave-company-button')).not.toBeNull()
  })

  it('toggles own new-invoice notifications for this company', async () => {
    vi.mocked(setMyNotifications).mockResolvedValue(undefined)
    await render(employeeAccess)

    const box = by('my-notifications') as HTMLInputElement
    expect(box.checked).toBe(true)
    expect(box.closest('label')?.textContent).toBe('Powiadomienia o nowych fakturach')
    await click(box)
    expect(setMyNotifications).toHaveBeenCalledExactlyOnceWith('a', false)
  })

  it('leaves the company after confirmation and goes home', async () => {
    vi.mocked(removeMember).mockResolvedValue(undefined)
    await render(employeeAccess)

    await click(by('leave-company-button'))
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('Opuścić firmę?')
    await confirm()
    expect(removeMember).toHaveBeenCalledExactlyOnceWith('a', 'me')
    expect(by('location')?.textContent).toBe('/')
  })

  it('the last owner leaving gets the API message', async () => {
    vi.mocked(removeMember).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify({ error: 'Firma musi mieć co najmniej jednego właściciela.' }), { status: 409, headers: { 'Content-Type': 'application/json' } })),
    )
    await render(ownerAccess)
    await click(by('leave-company-button'))
    await confirm()
    expect(vi.mocked(toast.error).mock.calls[0][0]).toBe('Firma musi mieć co najmniej jednego właściciela.')
    expect(by('location')?.textContent).toBe('/company/members')
  })

  it('inviting an existing account says Dostęp nadany', async () => {
    vi.mocked(createInvite).mockResolvedValue({ status: 'granted', displayName: 'Basia Nowak' })
    await render(ownerAccess)
    await inviteSomeone('  b@x.pl ', 'Accountant')

    expect(createInvite).toHaveBeenCalledExactlyOnceWith('a', { email: 'b@x.pl', role: 'Accountant' })
    expect(by('invite-granted')?.textContent).toContain('Dostęp nadany')
    expect(by('invite-granted')?.textContent).toContain('Basia Nowak ma już dostęp do tej firmy.')
    expect(listMembers).toHaveBeenCalledTimes(2)
  })

  it('inviting a new address says Zaproszenie oczekuje with a copyable link', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    vi.mocked(createInvite).mockResolvedValue({ status: 'pending', inviteId: 'inv-2', link: 'https://k.example/zaproszenie#tok', expiresAt: '2026-11-30T10:00:00Z', emailSent: false })
    await render(ownerAccess)
    await inviteSomeone('e@x.pl')

    expect(createInvite).toHaveBeenCalledExactlyOnceWith('a', { email: 'e@x.pl', role: 'Employee' })
    expect(by('invite-pending')?.textContent).toContain('Zaproszenie oczekuje')
    expect((by('invite-link') as HTMLInputElement).value).toBe('https://k.example/zaproszenie#tok')
    expect(by('invite-link-box')?.textContent).toContain('Przekaż link osobie zapraszanej (e@x.pl) dowolnym kanałem.')
    expect(by('invite-link-box')?.textContent).toContain('ważny do 30.11.2026')
    expect(by('invite-registration-off')).toBeNull()
    await click(by('invite-copy'))
    expect(writeText).toHaveBeenCalledExactlyOnceWith('https://k.example/zaproszenie#tok')
    expect(by('invite-copy')?.textContent).toBe('Skopiowano')
  })

  it('says the link was e-mailed, and warns when registration is off', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true, registrationAllowed: false })
    vi.mocked(createInvite).mockResolvedValue({ status: 'pending', inviteId: 'inv-2', link: 'https://k/zaproszenie#t', expiresAt: '2026-11-30T10:00:00Z', emailSent: true })
    await render(ownerAccess)
    await inviteSomeone('e@x.pl')

    expect(by('invite-link-box')?.textContent).toContain('Wysłaliśmy link e-mailem na e@x.pl.')
    expect(by('invite-registration-off')?.textContent).toBe('Rejestracja jest wyłączona - administrator musi utworzyć konto dla e@x.pl.')
  })

  it('shows API refusals in the dialog and checks the address first', async () => {
    vi.mocked(createInvite).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify({ error: 'Ta osoba ma już dostęp do tej firmy.' }), { status: 409, headers: { 'Content-Type': 'application/json' } })),
    )
    await render(ownerAccess)
    await inviteSomeone('zly-adres')
    expect(by('invite-error')?.textContent).toBe('Podaj poprawny adres e-mail.')
    expect(createInvite).not.toHaveBeenCalled()

    await type('invite-email', 'b@x.pl')
    await click(by('invite-submit'))
    expect(by('invite-error')?.textContent).toBe('Ta osoba ma już dostęp do tej firmy.')
  })

  it('lists pending invites with expiry; resend shows the new link, revoke asks first', async () => {
    vi.mocked(listInvites).mockResolvedValue([invite, { ...invite, id: 'inv-old', email: 'old@x.pl', isExpired: true }])
    vi.mocked(resendInvite).mockResolvedValue({ link: 'https://k/zaproszenie#new', expiresAt: '2026-11-30T10:00:00Z', emailSent: true })
    vi.mocked(revokeInvite).mockResolvedValue(undefined)
    await render(ownerAccess)

    const [fresh, old] = all('invite-row')
    expect(fresh.textContent).toContain('e@x.pl')
    expect(fresh.textContent).toContain('Pracownik · zaprasza Anna · ważne do 15.11.2026')
    expect(old.textContent).toContain('Wygasło')

    await click(old.querySelector<HTMLElement>('[data-testid="invite-resend"]'))
    expect(resendInvite).toHaveBeenCalledExactlyOnceWith('a', 'inv-old')
    expect((by('invite-link') as HTMLInputElement).value).toBe('https://k/zaproszenie#new')

    await click(all('invite-row')[0].querySelector<HTMLElement>('[data-testid="invite-revoke"]'))
    expect(revokeInvite).not.toHaveBeenCalled()
    await confirm()
    expect(revokeInvite).toHaveBeenCalledExactlyOnceWith('a', 'inv-1')
  })

  it('an administrator cannot resend or revoke an owner invite', async () => {
    vi.mocked(listInvites).mockResolvedValue([{ ...invite, role: 'Owner' }])
    await render(adminAccess)
    expect(by('invite-resend')).toBeNull()
    expect(by('invite-revoke')).toBeNull()
  })
})
