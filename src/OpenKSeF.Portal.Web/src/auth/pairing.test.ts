import { describe, expect, it } from 'vitest'

import { deviceVerificationUrl, normalizePairingCode } from './pairing'

describe('pairing code', () => {
  it.each(['abcd-efgh', 'ABCDEFGH', ' ab cd-ef gh ', 'abcdEFGH'])('normalises %j to ABCD-EFGH', (raw) => {
    expect(normalizePairingCode(raw)).toBe('ABCD-EFGH')
  })

  it.each(['', 'ABCD-EFG', 'ABCD-EFGHI', 'ABCD-1234', 'ĄBCD-EFGH', 'ABCD_EFGH'])('rejects %j', (raw) => {
    expect(normalizePairingCode(raw)).toBeNull()
  })

  it('builds the Keycloak device URL under the authority', () => {
    expect(deviceVerificationUrl('ABCD-EFGH', '/auth/realms/openksef')).toBe('/auth/realms/openksef/device?user_code=ABCD-EFGH')
    expect(deviceVerificationUrl('ABCD-EFGH', 'https://id.example.test/realms/openksef/')).toBe('https://id.example.test/realms/openksef/device?user_code=ABCD-EFGH')
  })
})
