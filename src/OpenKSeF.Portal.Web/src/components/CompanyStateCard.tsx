import { useQuery } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'

import { listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import type { TenantCredentialStatusResponse, TenantDashboardSummaryResponse } from '@/api/types'
import { companyName } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'
import type { CompanyState } from '@/hooks/useCompanyState'
import { formatDateTime } from '@/invoicing/format'
import { EmptyState } from './EmptyState'
import { Icon } from './Icon'

const NO_TOKEN_MESSAGE = 'Bez tokenu nie pobierzemy faktur ani nie wystawimy nowych.'
const PRIMARY = 'ui-button ui-button--primary ui-button--md'
const OUTLINE = 'ui-button ui-button--outline ui-button--md'

interface CompanyStateCardProps {
  state: Exclude<CompanyState, 'full'>
  place: 'start' | 'list'
  summary: TenantDashboardSummaryResponse | undefined
  credential: TenantCredentialStatusResponse | undefined
  attentionClear?: boolean
}

function AddToken(): ReactElement {
  return (
    <Link to="/company/token" className={PRIMARY}>
      <Icon name="key-round" /> Dodaj token KSeF
    </Link>
  )
}

export function CompanyStateCard({ state, place, summary, credential, attentionClear }: CompanyStateCardProps): ReactElement {
  const { company, tenantId } = useCompany()
  const nip = summary?.nip ?? company?.nip ?? ''
  const issuedQuery = useQuery({
    queryKey: ['issued-invoices', tenantId, 'any'],
    queryFn: () => listIssuedInvoices(tenantId, { page: 1, pageSize: 1 }),
    enabled: state === 'zero' && Boolean(tenantId),
  })
  const hasIssued = (issuedQuery.data?.totalCount ?? 0) > 0

  if (state === 'no-token') {
    if (place === 'list') {
      return (
        <EmptyState
          icon="key-round"
          title="Ta firma nie ma tokenu KSeF"
          message={`${NO_TOKEN_MESSAGE} Token z uprawnieniem odczytu i zapisu wygenerujesz w Aplikacji Podatnika KSeF.`}
          actions={
            <div className="ui-empty-state__actions">
              <AddToken />
            </div>
          }
        />
      )
    }
    return (
      <EmptyState
        icon="key-round"
        title="Firma dodana. Brakuje tokenu KSeF"
        message={NO_TOKEN_MESSAGE}
        actions={
          <ol className="start-steps">
            <li>
              <span className="start-steps__n">
                <Icon name="check" />
              </span>
              <span className="start-steps__text">
                <b>{company ? companyName(company) : nip}</b>
                <span>
                  NIP <span className="mono">{nip}</span>
                </span>
              </span>
            </li>
            <li>
              <span className="start-steps__n">1</span>
              <span className="start-steps__text">
                <b>Wklej token KSeF</b>
                <span>Token z uprawnieniem odczytu i zapisu, z Aplikacji Podatnika KSeF.</span>
              </span>
              <AddToken />
            </li>
            <li>
              <span className="start-steps__n">2</span>
              <span className="start-steps__text">
                <b>Pobierz faktury z KSeF</b>
                <span>Pierwsze pobranie obejmuje ostatnie 6 miesięcy. Fakturę zapłacisz kodem QR w aplikacji banku.</span>
              </span>
            </li>
            <li>
              <span className="start-steps__n">
                <Icon name="smartphone" />
              </span>
              <span className="start-steps__text">
                <b>Opcjonalnie: telefon</b>
                <span>Powiadomienia o nowych fakturach w aplikacji mobilnej.</span>
              </span>
              <Link to="/mobile-setup" className={OUTLINE}>
                Połącz telefon
              </Link>
            </li>
          </ol>
        }
      />
    )
  }

  if (state === 'first-fetch') {
    return (
      <EmptyState
        icon="clock"
        title="Czekamy na pierwsze pobranie z KSeF"
        message="Pobieranie uruchamia się samo co kilka godzin. Pierwsze obejmie ostatnie 6 miesięcy. Możesz uruchomić je teraz przyciskiem Pobierz z KSeF albo zamknąć stronę."
        actions={
          place === 'start' ? (
            <p className="start-note">
              <Icon name="circle-alert" />
              <span>Pobrane faktury będą nieopłacone, dopóki ich nie oznaczysz. Także te już zapłacone.</span>
            </p>
          ) : null
        }
      />
    )
  }

  if (state === 'first-fetch-late') {
    return (
      <EmptyState
        icon="circle-alert"
        title="Nie pobraliśmy jeszcze faktur."
        message={`Token dodano ${formatDateTime(credential?.lastUpdatedAt)}.`}
        actions={
          <div className="ui-empty-state__actions">
            <Link to="/company/token" className={PRIMARY}>
              <Icon name="key-round" /> Sprawdź token KSeF
            </Link>
          </div>
        }
      />
    )
  }

  const fetchedLine = `Pobrano ${formatDateTime(summary?.lastSuccessfulSync)}. KSeF nie zwrócił faktur zakupu dla NIP ${nip}.`
  const issueLabel = hasIssued ? 'Nowa faktura' : 'Wystaw pierwszą fakturę'
  if (place === 'list') {
    return (
      <EmptyState
        icon="inbox"
        title="Brak faktur otrzymanych w KSeF"
        message={fetchedLine}
        actions={
          <div className="ui-empty-state__actions">
            <Link to="/issued/new" className={OUTLINE}>
              {issueLabel}
            </Link>
          </div>
        }
      />
    )
  }
  return (
    <EmptyState
      icon="inbox"
      title={hasIssued ? 'Brak faktur otrzymanych w KSeF' : 'Brak faktur do pokazania'}
      message={attentionClear ? `${fetchedLine} Nic nie wymaga uwagi.` : fetchedLine}
      actions={
        <div className="ui-empty-state__actions">
          <Link to="/issued/new" className={PRIMARY}>
            <Icon name="plus" /> {issueLabel}
          </Link>
          <Link to="/mobile-setup" className={OUTLINE}>
            <Icon name="smartphone" /> Połącz telefon
          </Link>
        </div>
      }
    />
  )
}
