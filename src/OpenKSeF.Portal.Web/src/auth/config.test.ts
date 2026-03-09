import { describe, expect, it } from 'vitest'

import { getOidcConfig } from './config'

describe('getOidcConfig', () => {
  it('builds config with defaults', () => {
    const config = getOidcConfig({} as unknown as ImportMetaEnv, 'http://localhost:5173')

    expect(config.authority).toBe('/auth/realms/openksef')
    expect(config.client_id).toBe('openksef-portal-web')
    expect(config.redirect_uri).toBe('http://localhost:5173/callback')
    expect(config.silent_redirect_uri).toBe('http://localhost:5173/silent-callback')
    expect(config.post_logout_redirect_uri).toBe('http://localhost:5173')
    expect(config.response_type).toBe('code')
    expect(config.scope).toBe('openid profile email')
    expect(config.automaticSilentRenew).toBe(true)
    expect(config.accessTokenExpiringNotificationTimeInSeconds).toBe(60)
  })

  it('uses configured authority and client id from environment', () => {
    const config = getOidcConfig(
      {
        VITE_AUTH_AUTHORITY: 'https://id.example.test/realms/openksef',
        VITE_AUTH_CLIENT_ID: 'custom-client',
      } as unknown as ImportMetaEnv,
      'https://portal.example.test',
    )

    expect(config.authority).toBe('https://id.example.test/realms/openksef')
    expect(config.client_id).toBe('custom-client')
    expect(config.redirect_uri).toBe('https://portal.example.test/callback')
    expect(config.silent_redirect_uri).toBe('https://portal.example.test/silent-callback')
    expect(config.post_logout_redirect_uri).toBe('https://portal.example.test')
  })
})
