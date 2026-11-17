import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type FormEvent, type ReactElement } from 'react'
import toast from 'react-hot-toast'
import { useNavigate } from 'react-router-dom'

import {
  changeMemberRole,
  createInvite,
  listInvites,
  listMembers,
  removeMember,
  resendInvite,
  revokeInvite,
  setMyNotifications,
} from '@/api/endpoints/tenants'
import type { CreateInviteResponse, TenantInviteResponse, TenantMemberResponse, TenantResponse, TenantRole } from '@/api/types'
import { useAuth } from '@/auth/useAuth'
import { can, ROLE_LABELS } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'
import { Button } from '@/components/Button'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorBanner } from '@/components/ErrorBanner'
import { Icon } from '@/components/Icon'
import { Skeleton } from '@/components/Skeleton'
import { useSetupStatus } from '@/hooks/useSetupStatus'
import { formatWarsawDate } from '@/invoicing/format'

const ROLES = Object.keys(ROLE_LABELS) as TenantRole[]
const errorText = (error: unknown): string => (error instanceof Error ? error.message : 'Operacja nie powiodła się.')
const personName = (member: TenantMemberResponse): string => member.displayName || member.email || 'Użytkownik bez nazwy'

export function CompanyMembersPage(): ReactElement {
  const { company } = useCompany()
  return company ? <MembersTab key={company.id} company={company} /> : <Skeleton lines={6} />
}

function MembersTab({ company }: { company: TenantResponse }): ReactElement {
  const tenantId = company.id
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { user } = useAuth()
  const myId = user?.profile.sub
  const manage = can(company, 'members.manage')
  const grantsOwner = manage && company.role === 'Owner'
  const canManageRole = (role: TenantRole): boolean => manage && (grantsOwner || role !== 'Owner')
  const [inviteOpen, setInviteOpen] = useState(false)
  const [leaveOpen, setLeaveOpen] = useState(false)
  const [removing, setRemoving] = useState<TenantMemberResponse | null>(null)
  const [revoking, setRevoking] = useState<TenantInviteResponse | null>(null)
  const [ownRole, setOwnRole] = useState<TenantRole | null>(null)
  const [resent, setResent] = useState<{ email: string; link: string; expiresAt: string; emailSent: boolean } | null>(null)

  const members = useQuery({ queryKey: ['tenant-members', tenantId], queryFn: () => listMembers(tenantId), enabled: manage })
  const myEmail = user?.profile.email
  const self = members.data?.find((m) => m.userId === myId)
  const leaveClearsEmail = self
    ? self.receivesCompanyNotificationEmail === true
    : Boolean(myEmail && company.notificationEmail?.trim().toLowerCase() === myEmail.trim().toLowerCase())
  const invites = useQuery({ queryKey: ['tenant-invites', tenantId], queryFn: () => listInvites(tenantId), enabled: manage })
  const refreshLists = async (): Promise<void> => {
    await queryClient.invalidateQueries({ queryKey: ['tenant-members', tenantId] })
    await queryClient.invalidateQueries({ queryKey: ['tenant-invites', tenantId] })
  }

  const notifications = useMutation({
    mutationFn: (enabled: boolean) => setMyNotifications(tenantId, enabled),
    onSuccess: (_result, enabled) => toast.success(enabled ? 'Powiadomienia włączone.' : 'Powiadomienia wyłączone.'),
    onError: (error) => toast.error(errorText(error)),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['tenants'] }),
  })

  const leave = useMutation({
    mutationFn: () => removeMember(tenantId, myId ?? ''),
    onSuccess: async () => {
      toast.success('Opuszczono firmę.')
      setLeaveOpen(false)
      await queryClient.invalidateQueries()
      void navigate('/')
    },
    onError: (error) => {
      setLeaveOpen(false)
      toast.error(errorText(error), { duration: 10_000 })
    },
  })

  const remove = useMutation({
    mutationFn: (member: TenantMemberResponse) => removeMember(tenantId, member.userId),
    onSuccess: () => toast.success('Dostęp usunięty.'),
    onError: (error) => toast.error(errorText(error), { duration: 10_000 }),
    onSettled: async () => {
      setRemoving(null)
      await refreshLists()
    },
  })

  const changeRole = useMutation({
    mutationFn: ({ member, role }: { member: TenantMemberResponse; role: TenantRole }) => changeMemberRole(tenantId, member.userId, role),
    onSuccess: async (_result, { member }) => {
      toast.success('Rola zmieniona.')
      if (member.userId === myId) await queryClient.invalidateQueries({ queryKey: ['tenants'] })
    },
    onError: (error) => toast.error(errorText(error), { duration: 10_000 }),
    onSettled: async () => {
      setOwnRole(null)
      await refreshLists()
    },
  })

  const resend = useMutation({
    mutationFn: (invite: TenantInviteResponse) => resendInvite(tenantId, invite.id),
    onSuccess: (result, invite) => setResent({ email: invite.email, ...result }),
    onError: (error) => toast.error(errorText(error), { duration: 10_000 }),
    onSettled: () => refreshLists(),
  })

  const revoke = useMutation({
    mutationFn: (invite: TenantInviteResponse) => revokeInvite(tenantId, invite.id),
    onSuccess: () => toast.success('Zaproszenie odwołane.'),
    onError: (error) => toast.error(errorText(error)),
    onSettled: async () => {
      setRevoking(null)
      await refreshLists()
    },
  })

  return (
    <div className="company-grid">
      <section className="ui-card" aria-labelledby="my-access-title">
        <h2 id="my-access-title">Twój dostęp</h2>
        <p data-testid="my-role">
          Twoja rola: <b>{ROLE_LABELS[company.role]}</b>
        </p>
        {can(company, 'notifications.self') ? (
          <div>
            <label className="inv-check">
              <input
                type="checkbox"
                data-testid="my-notifications"
                aria-describedby="my-notifications-hint"
                checked={company.notificationsEnabled}
                disabled={notifications.isPending}
                onChange={(event) => notifications.mutate(event.target.checked)}
              />
              <span>Powiadomienia o nowych fakturach</span>
            </label>
            <p className="company-muted" id="my-notifications-hint">
              Dotyczy tej firmy i Twoich urządzeń. Inne osoby ustawiają to same.
            </p>
          </div>
        ) : null}
        <ul className="danger-list">
          <li>
            <span>
              <b>Opuść firmę</b>
              <span>Stracisz dostęp do faktur tej firmy. Wrócisz tylko przez nowe zaproszenie.</span>
            </span>
            <button
              type="button"
              className="ui-button ui-button--danger-outline ui-button--md"
              data-testid="leave-company-button"
              onClick={() => setLeaveOpen(true)}
            >
              Opuść firmę
            </button>
          </li>
        </ul>
      </section>

      {manage ? (
        <section className="ui-card" aria-labelledby="members-title">
          <div className="members-head">
            <h2 id="members-title">Osoby z dostępem</h2>
            <Button data-testid="invite-open" onClick={() => setInviteOpen(true)}>
              <Icon name="plus" size={16} />
              Zaproś osobę
            </Button>
          </div>
          {members.error ? <ErrorBanner message={errorText(members.error)} onRetry={() => void members.refetch()} /> : null}
          {members.data ? (
            <ul className="danger-list" data-testid="members-list">
              {members.data.map((member) => {
                const me = member.userId === myId
                const editable = canManageRole(member.role)
                return (
                  <li key={member.userId} data-testid="member-row">
                    <span>
                      <b>
                        {personName(member)}
                        {me ? ' (Ty)' : ''}
                      </b>
                      {member.displayName && member.email ? <span>{member.email}</span> : null}
                    </span>
                    {editable ? (
                      <select
                        className="members-role"
                        aria-label={`Rola: ${personName(member)}`}
                        data-testid="member-role"
                        value={member.role}
                        disabled={changeRole.isPending}
                        onChange={(event) => {
                          const role = event.target.value as TenantRole
                          if (me) setOwnRole(role)
                          else changeRole.mutate({ member, role })
                        }}
                      >
                        {ROLES.filter((role) => canManageRole(role)).map((role) => (
                          <option key={role} value={role}>
                            {ROLE_LABELS[role]}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <span className="inv-badge inv-badge--neutral" data-testid="member-role-label">
                        {ROLE_LABELS[member.role]}
                      </span>
                    )}
                    {editable && !me ? (
                      <button
                        type="button"
                        className="ui-button ui-button--danger-outline ui-button--sm"
                        data-testid="member-remove"
                        aria-label={`Usuń dostęp: ${personName(member)}`}
                        onClick={() => setRemoving(member)}
                      >
                        Usuń
                      </button>
                    ) : null}
                  </li>
                )
              })}
            </ul>
          ) : members.isLoading ? (
            <Skeleton lines={3} />
          ) : null}
        </section>
      ) : null}

      {manage ? (
        <section className="ui-card" aria-labelledby="invites-title">
          <h2 id="invites-title">Oczekujące zaproszenia</h2>
          {resent ? (
            <InviteLinkBox email={resent.email} link={resent.link} expiresAt={resent.expiresAt} emailSent={resent.emailSent} />
          ) : null}
          {invites.error ? <ErrorBanner message={errorText(invites.error)} onRetry={() => void invites.refetch()} /> : null}
          {invites.data?.length === 0 ? <p className="company-muted">Brak oczekujących zaproszeń.</p> : null}
          {invites.data?.length ? (
            <ul className="danger-list" data-testid="invites-list">
              {invites.data.map((invite) => (
                <li key={invite.id} data-testid="invite-row">
                  <span>
                    <b>{invite.email}</b>
                    <span>
                      {ROLE_LABELS[invite.role]}
                      {invite.invitedByName ? ` · zaprasza ${invite.invitedByName}` : ''}
                      {invite.isExpired ? '' : ` · ważne do ${formatWarsawDate(invite.expiresAt)}`}
                    </span>
                  </span>
                  {invite.isExpired ? <span className="inv-badge inv-badge--warning">Wygasło</span> : null}
                  {canManageRole(invite.role) ? (
                    <>
                      <Button
                        variant="outline"
                        size="sm"
                        data-testid="invite-resend"
                        aria-label={`Wyślij ponownie: ${invite.email}`}
                        disabled={resend.isPending}
                        onClick={() => resend.mutate(invite)}
                      >
                        Wyślij ponownie
                      </Button>
                      <button
                        type="button"
                        className="ui-button ui-button--danger-outline ui-button--sm"
                        data-testid="invite-revoke"
                        aria-label={`Odwołaj zaproszenie: ${invite.email}`}
                        onClick={() => setRevoking(invite)}
                      >
                        Odwołaj
                      </button>
                    </>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : null}
        </section>
      ) : null}

      {inviteOpen ? (
        <InviteDialog
          tenantId={tenantId}
          roles={ROLES.filter((role) => canManageRole(role))}
          onInvited={refreshLists}
          onClose={() => setInviteOpen(false)}
        />
      ) : null}

      <ConfirmDialog
        open={leaveOpen}
        title="Opuścić firmę?"
        message="Stracisz dostęp do faktur tej firmy. Wrócisz tylko przez nowe zaproszenie."
        confirmLabel="Opuść firmę"
        pendingLabel="Opuszczanie…"
        variant="danger"
        isPending={leave.isPending}
        onConfirm={() => leave.mutate()}
        onCancel={() => setLeaveOpen(false)}
      >
        {leaveClearsEmail ? (
          <p className="company-notice" data-testid="leave-notification-email" role="status">
            Twój adres {company.notificationEmail ?? myEmail} przestanie dostawać powiadomienia e-mail o nowych fakturach tej firmy.
          </p>
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={ownRole !== null}
        title="Zmienić swoją rolę?"
        message={`Nowa rola: ${ownRole ? ROLE_LABELS[ownRole] : ''}. Możesz stracić możliwość zarządzania firmą.`}
        confirmLabel="Zmień rolę"
        pendingLabel="Zapisywanie…"
        variant="danger"
        isPending={changeRole.isPending}
        onConfirm={() => {
          if (self && ownRole) changeRole.mutate({ member: self, role: ownRole })
        }}
        onCancel={() => setOwnRole(null)}
      />

      <ConfirmDialog
        open={removing !== null}
        title="Usunąć dostęp?"
        message={`${removing ? personName(removing) : ''} straci dostęp do tej firmy.`}
        confirmLabel="Usuń dostęp"
        variant="danger"
        isPending={remove.isPending}
        onConfirm={() => removing && remove.mutate(removing)}
        onCancel={() => setRemoving(null)}
      >
        {removing?.receivesCompanyNotificationEmail ? (
          <p className="company-notice" data-testid="remove-notification-email" role="status">
            Adres {company.notificationEmail ?? removing.email} przestanie dostawać powiadomienia e-mail o nowych fakturach tej firmy.
          </p>
        ) : null}
      </ConfirmDialog>

      <ConfirmDialog
        open={revoking !== null}
        title="Odwołać zaproszenie?"
        message={`Link wysłany do ${revoking?.email ?? ''} przestanie działać.`}
        confirmLabel="Odwołaj zaproszenie"
        pendingLabel="Odwoływanie…"
        variant="danger"
        isPending={revoke.isPending}
        onConfirm={() => revoking && revoke.mutate(revoking)}
        onCancel={() => setRevoking(null)}
      />
    </div>
  )
}

function InviteLinkBox({ email, link, expiresAt, emailSent }: { email: string; link: string; expiresAt: string; emailSent: boolean }): ReactElement {
  const [copied, setCopied] = useState(false)
  return (
    <div className="ui-form-group" data-testid="invite-link-box">
      <label htmlFor="invite-link">Link do zaproszenia</label>
      <div className="invite-link">
        <input id="invite-link" data-testid="invite-link" className="mono" type="text" readOnly value={link} onFocus={(event) => event.target.select()} />
        <Button
          variant="outline"
          data-testid="invite-copy"
          onClick={() => void navigator.clipboard?.writeText(link).then(() => setCopied(true))}
        >
          <Icon name={copied ? 'check' : 'copy'} size={16} />
          {copied ? 'Skopiowano' : 'Kopiuj link'}
        </Button>
      </div>
      <span className="ui-form-hint" role="status">
        {emailSent ? `Wysłaliśmy link e-mailem na ${email}. ` : `Przekaż link osobie zapraszanej (${email}) dowolnym kanałem. `}
        Link działa raz i jest ważny do {formatWarsawDate(expiresAt)}.
      </span>
    </div>
  )
}

interface InviteDialogProps {
  tenantId: string
  roles: TenantRole[]
  onInvited: () => Promise<void>
  onClose: () => void
}

function InviteDialog({ tenantId, roles, onInvited, onClose }: InviteDialogProps): ReactElement {
  const setup = useSetupStatus()
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<TenantRole>('Employee')
  const [formError, setFormError] = useState<string | null>(null)
  const [result, setResult] = useState<{ email: string; response: CreateInviteResponse } | null>(null)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const invite = useMutation({
    mutationFn: () => createInvite(tenantId, { email: email.trim(), role }),
    onSuccess: async (response) => {
      setResult({ email: email.trim(), response })
      await onInvited()
    },
    onError: (error) => setFormError(errorText(error)),
  })

  function onSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault()
    const error = /^\S+@\S+\.\S+$/.test(email.trim()) ? null : 'Podaj poprawny adres e-mail.'
    setFormError(error)
    if (!error) invite.mutate()
  }

  function again(): void {
    setResult(null)
    setEmail('')
    setRole('Employee')
  }

  const response = result?.response

  return (
    <div
      className="ui-modal-backdrop"
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <form className="ui-modal" role="dialog" aria-modal="true" aria-labelledby="invite-title" onSubmit={onSubmit} noValidate>
        <div className="ui-modal-header">
          <h2 className="ui-modal-title" id="invite-title">
            Zaproś osobę
          </h2>
          <button type="button" className="ui-modal-close" aria-label="Zamknij" onClick={onClose}>
            ✕
          </button>
        </div>
        <div className="ui-modal-body">
          {result && response ? (
            response.status === 'granted' ? (
              <div className="inv-alert inv-alert--success" role="status" data-testid="invite-granted">
                <Icon name="circle-check" />
                <span>
                  <b>Dostęp nadany</b>
                  <br />
                  {response.displayName || result.email} ma już dostęp do tej firmy.
                </span>
              </div>
            ) : (
              <>
                <div className="inv-alert inv-alert--info" role="status" data-testid="invite-pending">
                  <Icon name="mail" />
                  <span>
                    <b>Zaproszenie oczekuje</b>
                    <br />
                    Dostęp dostanie po zalogowaniu na {result.email} i przyjęciu zaproszenia.
                  </span>
                </div>
                {response.link && response.expiresAt ? (
                  <InviteLinkBox email={result.email} link={response.link} expiresAt={response.expiresAt} emailSent={response.emailSent ?? false} />
                ) : null}
                {setup.data?.registrationAllowed === false ? (
                  <p className="inv-alert inv-alert--warning" role="status" data-testid="invite-registration-off">
                    Rejestracja jest wyłączona - administrator musi utworzyć konto dla {result.email}.
                  </p>
                ) : null}
              </>
            )
          ) : (
            <>
              <div className="ui-form-group">
                <label htmlFor="invite-email">Adres e-mail</label>
                <input
                  id="invite-email"
                  data-testid="invite-email"
                  type="email"
                  autoComplete="off"
                  maxLength={320}
                  autoFocus
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                />
              </div>
              <div className="ui-form-group">
                <label htmlFor="invite-role">Rola</label>
                <select id="invite-role" data-testid="invite-role" value={role} onChange={(event) => setRole(event.target.value as TenantRole)}>
                  {roles.map((value) => (
                    <option key={value} value={value}>
                      {ROLE_LABELS[value]}
                    </option>
                  ))}
                </select>
              </div>
              {formError ? (
                <div className="ui-form-error" role="alert" data-testid="invite-error">
                  {formError}
                </div>
              ) : null}
            </>
          )}
        </div>
        <div className="ui-modal-footer">
          {result ? (
            <>
              <Button variant="outline" data-testid="invite-again" onClick={again}>
                Zaproś kolejną osobę
              </Button>
              <Button data-testid="invite-close" onClick={onClose}>
                Gotowe
              </Button>
            </>
          ) : (
            <>
              <Button variant="outline" onClick={onClose}>
                Anuluj
              </Button>
              <Button type="submit" data-testid="invite-submit" disabled={invite.isPending}>
                {invite.isPending ? 'Wysyłanie…' : 'Zaproś'}
              </Button>
            </>
          )}
        </div>
      </form>
    </div>
  )
}
