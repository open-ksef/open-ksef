import type { TenantHistoryEventResponse, TenantRole } from '@/api/types'
import { ROLE_LABELS } from '@/company/CompanyContext'

const LABELS: Record<string, string> = {
  'invoice.paid': 'Oznaczono fakturę jako opłaconą',
  'invoice.unpaid': 'Oznaczono fakturę jako nieopłaconą',
  'invoices.exported': 'Pobrano eksport faktur',
  'credential.set': 'Zapisano dane dostępowe KSeF',
  'credential.deleted': 'Usunięto dane dostępowe KSeF',
  'invite.created': 'Wysłano zaproszenie',
  'invite.resent': 'Ponownie wysłano zaproszenie',
  'invite.revoked': 'Odwołano zaproszenie',
  'invite.accepted': 'Przyjęto zaproszenie',
  'access.granted': 'Nadano dostęp',
  'member.roleChanged': 'Zmieniono rolę',
  'member.removed': 'Usunięto osobę z firmy',
  'member.left': 'Osoba opuściła firmę',
  'tenant.notificationEmailCleared': 'Usunięto adres e-mail do powiadomień',
  'tenant.ownershipClaimed': 'Przejęto firmę tokenem KSeF',
}

const CREDENTIALS: Record<string, string> = { Token: 'token', Certificate: 'certyfikat' }

const role = (code: string | null): string | null => (code ? ROLE_LABELS[code as TenantRole] ?? code : null)

export const historyLabel = (action: string): string => LABELS[action] ?? action

function exportDetails(target: string): string {
  const fields = Object.fromEntries(target.split(';').map((pair) => pair.split('=') as [string, string]))
  const directions = (fields.kierunki ?? '').split(',')
  const counts = [
    directions.includes('sprzedaz') ? `sprzedaż: ${fields.sprzedaz}` : null,
    directions.includes('zakup') ? `zakup: ${fields.zakup}` : null,
  ].filter(Boolean)
  const period = fields.od && fields.do ? `okres ${fields.od} – ${fields.do}` : 'cały okres'
  return [period, counts.join(', ')].filter(Boolean).join(', ')
}

export function historyDetails(event: TenantHistoryEventResponse): string {
  const person = event.targetName ?? event.targetEmail
  const join = (...parts: (string | null | undefined)[]): string => parts.filter(Boolean).join(', ')
  switch (event.action) {
    case 'invoice.paid':
    case 'invoice.unpaid':
      return event.target ?? '—'
    case 'invoices.exported':
      return event.target ? exportDetails(event.target) : ''
    case 'credential.set':
      return event.target ? CREDENTIALS[event.target] ?? event.target : ''
    case 'invite.created':
    case 'invite.resent':
    case 'invite.revoked':
    case 'invite.accepted':
      return join(event.targetEmail, role(event.target))
    case 'access.granted':
    case 'member.removed':
      return join(person, role(event.target))
    case 'member.roleChanged': {
      const [from, to] = (event.target ?? '').split('->')
      return join(person, from && to ? `${role(from)} → ${role(to)}` : null)
    }
    case 'member.left':
      return role(event.target) ?? ''
    case 'tenant.notificationEmailCleared':
      return event.targetEmail ?? ''
    default:
      return ''
  }
}
