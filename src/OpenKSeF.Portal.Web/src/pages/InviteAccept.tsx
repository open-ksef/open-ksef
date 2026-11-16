import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { Link, useParams } from 'react-router-dom'

import { acceptInvite } from '@/api/endpoints/tenants'
import { useAuth } from '@/auth/useAuth'
import { ROLE_LABELS } from '@/company/CompanyContext'
import { BrandMark } from '@/components/BrandMark'

export function InviteAcceptPage(): ReactElement {
  const { token = '' } = useParams()
  const { user, logout } = useAuth()
  const queryClient = useQueryClient()
  const accept = useMutation({
    mutationFn: () => acceptInvite(token),
    onSuccess: () => queryClient.invalidateQueries(),
  })
  const email = user?.profile.email

  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="auth-logo">
          <BrandMark size={40} />
        </div>
        <h1 className="auth-card__title">Zaproszenie do firmy</h1>
        {accept.data ? (
          <>
            <p className="auth-card__desc" role="status" data-testid="invite-accepted">
              Masz dostęp do firmy <b>{accept.data.displayName}</b>. Twoja rola: {ROLE_LABELS[accept.data.role]}.
            </p>
            <div className="auth-card__actions">
              <Link
                to={`/?tenantId=${encodeURIComponent(accept.data.tenantId)}`}
                className="ui-button ui-button--primary auth-card__btn"
                data-testid="invite-open-company"
              >
                Przejdź do firmy
              </Link>
            </div>
          </>
        ) : (
          <>
            <p className="auth-card__desc">
              {email ? (
                <>
                  Jesteś zalogowany jako <b data-testid="invite-signed-in-as">{email}</b>. Zaproszenie zadziała tylko dla adresu, na który je wysłano.
                </>
              ) : (
                'Zaproszenie zadziała tylko dla adresu, na który je wysłano.'
              )}
            </p>
            {accept.error ? (
              <div className="ui-form-error auth-card__error" role="alert" data-testid="invite-accept-error">
                <span className="ui-form-error__icon" aria-hidden="true">!</span>
                {accept.error.message}
              </div>
            ) : null}
            <div className="auth-card__actions">
              <button
                type="button"
                className="ui-button ui-button--primary auth-card__btn"
                data-testid="invite-accept"
                disabled={accept.isPending || !token}
                onClick={() => accept.mutate()}
              >
                {accept.isPending ? 'Przyjmowanie…' : 'Przyjmij zaproszenie'}
              </button>
              <button type="button" className="ui-button ui-button--outline auth-card__btn" onClick={() => void logout()}>
                Wyloguj się
              </button>
            </div>
          </>
        )}
      </div>
    </main>
  )
}
