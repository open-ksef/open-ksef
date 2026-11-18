import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const src = resolve(import.meta.dirname, 'src')
const ui = readFileSync(resolve(src, 'components/ui.css'), 'utf8').replace(/\r\n/g, '\n')

function tokensOf(block: string): Record<string, string> {
  return Object.fromEntries([...block.matchAll(/(--ui-[a-z0-9-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2]]))
}

const light = tokensOf(ui.slice(ui.indexOf(':root {'), ui.indexOf("\n}\n", ui.indexOf(':root {'))))
const mediaStart = ui.indexOf(":root:not([data-theme='light'])")
const darkMedia = tokensOf(ui.slice(mediaStart, ui.indexOf('\n  }\n', mediaStart)))
const forcedStart = ui.indexOf(":root[data-theme='dark']")
const darkForced = tokensOf(ui.slice(forcedStart, ui.indexOf('\n}\n', forcedStart)))

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const [r, g, b] = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

describe('design tokens', () => {
  it('defines every --ui-* token that src references', () => {
    const defined = new Set([...ui.matchAll(/(--ui-[a-z0-9-]+):/g)].map((m) => m[1]))
    const files = readdirSync(src, { recursive: true, encoding: 'utf8' }).filter((f) => /\.(css|tsx?)$/.test(f))
    const undefinedRefs = files.flatMap((f) =>
      [...readFileSync(resolve(src, f), 'utf8').matchAll(/var\(\s*(--ui-[a-z0-9-]+)/g)].map((m) => m[1]).filter((t) => !defined.has(t)).map((t) => `${f}: ${t}`),
    )
    expect(undefinedRefs).toEqual([])
  })

  it('keeps CSS font sizes on the type scale', () => {
    const files = readdirSync(src, { recursive: true, encoding: 'utf8' }).filter((f) => f.endsWith('.css'))
    const sizes = new Set(files.flatMap((f) => [...readFileSync(resolve(src, f), 'utf8').matchAll(/font-size:\s*(\d+)px/g)].map((m) => Number(m[1]))))
    expect([...sizes].filter((size) => ![12, 13, 14, 16, 18, 22, 28].includes(size))).toEqual([])
  })

  it('keeps the two dark sets identical', () => {
    expect(Object.keys(darkMedia).length).toBeGreaterThan(20)
    expect(darkForced).toEqual(darkMedia)
  })

  it('overrides every colour token in dark', () => {
    const notColours = /^--ui-(sidebar|font|shadow|radius|transition|breakpoint)/
    const colours = Object.keys(light).filter((name) => !notColours.test(name))
    expect(Object.keys(darkMedia).sort()).toEqual(colours.sort())
  })

  it.each([
    ['light', light],
    ['dark', darkMedia],
  ])('primary button text is at least 4.5:1 in %s', (_, set) => {
    expect(contrast(set['--ui-on-primary'], set['--ui-primary'])).toBeGreaterThanOrEqual(4.5)
    expect(contrast(set['--ui-on-primary'], set['--ui-primary-hover'])).toBeGreaterThanOrEqual(4.5)
  })

  it.each([
    ['primary', '--ui-on-primary'],
    ['secondary', '--ui-primary'],
    ['outline', '--ui-text'],
    ['danger', '--ui-on-error'],
    ['ghost', '--ui-primary'],
  ])('keeps the %s button label colour on hover, also on a link', (variant, token) => {
    const rule = ui.match(new RegExp(`\\.ui-button--${variant}:hover \\{([^}]*)\\}`))
    expect(rule?.[1]).toContain(`color: var(${token});`)
  })

  it('does not let the plain button rule outrank component buttons', () => {
    expect(ui).not.toMatch(/^button:not\(/m)
  })

  it('shows the selected choice on the invoice form', () => {
    const invoicing = readFileSync(resolve(src, 'components/invoicing/invoicing.css'), 'utf8')
    const rule = invoicing.match(/\.inv-chips button\[aria-pressed='true'\] \{([^}]*)\}/)
    expect(rule?.[1]).toContain('background: var(--ui-primary-subtle);')
  })

  it('has no scoped token block in invoicing.css', () => {
    expect(readFileSync(resolve(src, 'components/invoicing/invoicing.css'), 'utf8')).not.toMatch(/--ui-[a-z0-9-]+:/)
  })

  it('underlines links in the list status line, which keep the color of the text around them', () => {
    const css = readFileSync(resolve(src, 'components/ui.css'), 'utf8')
    expect(css).toMatch(/\.received-sync--stale a \{[^}]*text-decoration: underline;/)
  })

  it('loads fonts locally', () => {
    const index = readFileSync(resolve(src, 'index.css'), 'utf8')
    expect(index).not.toMatch(/https?:/)
    for (const [, file] of index.matchAll(/url\('\.\/([^']+)'\)/g)) {
      expect(existsSync(resolve(src, file))).toBe(true)
    }
    expect(readFileSync(resolve(import.meta.dirname, 'index.html'), 'utf8')).not.toContain('fonts.g')
  })
})

describe('nip input', () => {
  it('sets no flex-basis, so a column .ui-form-group keeps it one line tall', () => {
    const rule = ui.slice(ui.indexOf('input.nip-input {'), ui.indexOf('}', ui.indexOf('input.nip-input {')))
    expect(rule).toContain('flex: none;')
    expect(rule).toContain('width: 144px;')
  })
})
