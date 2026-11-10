import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

import { TOKEN_REJECTIONS } from './src/invoicing/status'

const processor = readFileSync(resolve(import.meta.dirname, '../OpenKSeF.Sync/Issuing/IssuedInvoiceSendProcessor.cs'), 'utf8')
const stored = (name: string): string | undefined => processor.match(new RegExp(`${name} = "([^"]+)"`))?.[1]

describe('token rejection texts', () => {
  it('match the messages the send processor stores', () => {
    expect(TOKEN_REJECTIONS.slice(0, 4)).toEqual(['NoWritePermissionMessage', 'InvalidTokenMessage', 'MissingTokenMessage', 'InvalidCertificateMessage'].map(stored))
  })
})
