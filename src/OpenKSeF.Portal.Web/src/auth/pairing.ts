import { oidcConfig } from './config'

export function normalizePairingCode(raw: string): string | null {
  const code = raw.toUpperCase().replace(/[\s-]/g, '')
  return /^[A-Z]{8}$/.test(code) ? `${code.slice(0, 4)}-${code.slice(4)}` : null
}

export function deviceVerificationUrl(code: string, authority: string = oidcConfig.authority): string {
  return `${authority.replace(/\/+$/, '')}/device?user_code=${encodeURIComponent(code)}`
}
