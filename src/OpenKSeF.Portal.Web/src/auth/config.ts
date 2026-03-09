import type { UserManagerSettings } from 'oidc-client-ts'

const DEFAULT_AUTHORITY = '/auth/realms/openksef'
const DEFAULT_CLIENT_ID = 'openksef-portal-web'
const DEFAULT_SCOPE = 'openid profile email'

export function getOidcConfig(
  env: ImportMetaEnv = import.meta.env,
  origin: string = getDefaultOrigin(),
): UserManagerSettings {
  const authority = env.VITE_AUTH_AUTHORITY ?? DEFAULT_AUTHORITY
  const clientId = env.VITE_AUTH_CLIENT_ID ?? DEFAULT_CLIENT_ID

  return {
    authority,
    client_id: clientId,
    redirect_uri: `${origin}/callback`,
    silent_redirect_uri: `${origin}/silent-callback`,
    post_logout_redirect_uri: origin,
    response_type: 'code',
    scope: DEFAULT_SCOPE,
    automaticSilentRenew: true,
    accessTokenExpiringNotificationTimeInSeconds: 60,
  }
}

export const oidcConfig = getOidcConfig()

function getDefaultOrigin(): string {
  return globalThis.location?.origin ?? 'http://localhost:5173'
}
