import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const projectRoot = resolve(import.meta.dirname)

describe('Portal web invoice styling tokens', () => {
  it('defines invoice status and severity variables plus print scaffold', () => {
    const stylesheet = readFileSync(resolve(projectRoot, 'src/components/ui.css'), 'utf8')

    expect(stylesheet).toContain('--status-draft:')
    expect(stylesheet).toContain('--status-approved:')
    expect(stylesheet).toContain('--status-submitted:')
    expect(stylesheet).toContain('--status-accepted:')
    expect(stylesheet).toContain('--status-rejected:')
    expect(stylesheet).toContain('--severity-error:')
    expect(stylesheet).toContain('--severity-warning:')
    expect(stylesheet).toContain('@media print')
  })
})
