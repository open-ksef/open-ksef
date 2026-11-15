// Portal E2E on a running dev stack: hosted Keycloak login, 2FA, register, logout, session expiry, axe (serious+, contrast on) per page.
// usage (in src/OpenKSeF.Portal.Web): node e2e/portal.e2e.mjs
// env: BASE_URL (http://localhost:8080), KC_URL (http://localhost:8082/auth), KC_ADMIN_USER / KC_ADMIN_PASSWORD (admin / admin),
//      DATA_USER / DATA_PASSWORD (password-only user with invoices: enrolled in 2FA by the run, restored to password-only after),
//      BROWSER_CHANNEL (chrome | msedge, default chrome), SHOT_DIR (screenshots, off by default: dev data may be real)
import { createHmac, randomBytes } from 'node:crypto'
import { createRequire } from 'node:module'
import fs from 'node:fs'
import path from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright-core')
const axeSrc = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8')

const BASE = process.env.BASE_URL ?? 'http://localhost:8080'
const KC = process.env.KC_URL ?? 'http://localhost:8082/auth'
const SHOT_DIR = process.env.SHOT_DIR
const RUN = Date.now().toString(36)
const PASSWORD = `E2e-${randomBytes(8).toString('hex')}!`
const SERIOUS = new Set(['serious', 'critical'])

// Known serious+ violations, rule id -> backlog task. A new one fails the run.
const KNOWN = {}

const results = []
const violations = []
const created = []
let browser
let adminToken

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function admin(method, url, body) {
  const auth = await fetch(`${KC}/realms/master/protocol/openid-connect/token`, {
    method: 'POST',
    body: new URLSearchParams({ grant_type: 'password', client_id: 'admin-cli', username: process.env.KC_ADMIN_USER ?? 'admin', password: process.env.KC_ADMIN_PASSWORD ?? 'admin' }),
  })
  adminToken = (await auth.json()).access_token
  const res = await fetch(`${KC}/admin/realms/openksef${url}`, {
    method,
    headers: { Authorization: `Bearer ${adminToken}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) throw new Error(`${method} ${url}: ${res.status}`)
  const text = await res.text()
  return text ? JSON.parse(text) : res.headers.get('location')
}

async function createUser(email) {
  const location = await admin('POST', '/users', {
    username: email, email, enabled: true, emailVerified: true, firstName: 'E2E', lastName: 'Test',
    credentials: [{ type: 'password', value: PASSWORD, temporary: false }],
  })
  const id = location.split('/').pop()
  created.push(id)
  return id
}

async function restoreToPasswordOnly(id) {
  for (const c of await admin('GET', `/users/${id}/credentials`)) {
    if (c.type !== 'password') await admin('DELETE', `/users/${id}/credentials/${c.id}`)
  }
  await admin('POST', `/users/${id}/logout`)
  await admin('DELETE', `/attack-detection/brute-force/users/${id}`)
}

let lastCounter = -1
async function totp(secret) {
  let counter = Math.floor(Date.now() / 30000)
  // Keycloak rejects a code it already accepted, so wait for the next window.
  if (counter <= lastCounter) {
    await sleep((lastCounter + 1) * 30000 - Date.now() + 500)
    counter = Math.floor(Date.now() / 30000)
  }
  lastCounter = counter
  const buf = Buffer.alloc(8)
  buf.writeBigUInt64BE(BigInt(counter))
  const h = createHmac('sha1', Buffer.from(secret)).update(buf).digest()
  const o = h[h.length - 1] & 15
  return String((h.readUInt32BE(o) & 0x7fffffff) % 1000000).padStart(6, '0')
}

async function shot(page, name) {
  if (!SHOT_DIR) return
  fs.mkdirSync(SHOT_DIR, { recursive: true })
  await page.screenshot({ path: path.join(SHOT_DIR, `T-100-${name}.png`), fullPage: true }).catch(() => {})
}

async function check(name, fn, page) {
  try {
    await fn()
    results.push({ name, ok: true })
    console.log(`PASS ${name}`)
  } catch (error) {
    results.push({ name, ok: false, detail: error.message.split('\n')[0] })
    console.log(`FAIL ${name}: ${error.message.split('\n')[0]}`)
    if (page) await shot(page, `fail-${name}`)
  }
}

function expect(condition, message) {
  if (!condition) throw new Error(message)
}

async function settle(page) {
  await page.waitForLoadState('networkidle').catch(() => {})
  await page.waitForFunction(() => !/Ładowanie|Sprawdzanie|Przekierowanie/.test(document.querySelector('main')?.textContent ?? ''), null, { timeout: 15000 }).catch(() => {})
  await page.waitForFunction(() => !document.querySelector('.ui-skeleton, [aria-busy="true"]'), null, { timeout: 15000 }).catch(() => {})
}

async function axe(page, name) {
  await settle(page)
  // A colour transition after the scheme switch makes axe read half-light, half-dark pairs.
  await page.addStyleTag({ content: '*,*::before,*::after{transition:none!important;animation:none!important}' })
  for (const scheme of ['light', 'dark']) {
    await page.emulateMedia({ colorScheme: scheme, reducedMotion: 'reduce' })
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))))
    if (!(await page.evaluate(() => 'axe' in window))) await page.addScriptTag({ content: axeSrc })
    const found = await page.evaluate(async () =>
      (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] })).violations.map((v) => ({
        id: v.id, impact: v.impact, count: v.nodes.length, targets: v.nodes.slice(0, 3).map((n) => `${n.target.join(' ')} ${n.any[0]?.message ?? ''}`.trim()),
      })),
    )
    await shot(page, `${name}_${scheme}`)
    for (const v of found.filter((x) => SERIOUS.has(x.impact))) violations.push({ page: name, scheme, ...v, known: KNOWN[`${name}:${v.id}`] ?? KNOWN[v.id] ?? null })
  }
  await page.emulateMedia({ colorScheme: 'light' })
  const fresh = violations.filter((v) => v.page === name && !v.known)
  expect(!fresh.length, `axe serious+: ${[...new Set(fresh.map((v) => `${v.id}/${v.scheme}`))].join(' ')}`)
}

async function fresh() {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'pl-PL', bypassCSP: true })
  return { ctx, page: await ctx.newPage() }
}

async function atKeycloakLogin(page) {
  await page.waitForSelector('#kc-form-login', { timeout: 20000 })
}

async function signIn(page, email, password) {
  await page.fill('#username', email)
  await page.fill('#password', password)
  await Promise.all([page.waitForNavigation(), page.click('#kc-form-login button[type=submit]')])
}

async function enrol(page, prefix) {
  await page.waitForSelector('input[name=totpSecret]', { state: 'attached' })
  if (prefix) await axe(page, `${prefix}otp-setup`)
  const secret = await page.inputValue('input[name=totpSecret]')
  await page.fill('#totp', await totp(secret))
  await Promise.all([page.waitForNavigation(), page.click('.auth-card form button[type=submit]')])
  await page.waitForSelector('#kcRecoveryCodesConfirmationCheck')
  if (prefix) await axe(page, `${prefix}recovery-setup`)
  await page.check('#kcRecoveryCodesConfirmationCheck')
  await Promise.all([page.waitForNavigation(), page.click('.auth-card form button[type=submit]')])
  return secret
}

async function inPortal(page, pathname) {
  await page.waitForURL((u) => u.origin === new URL(BASE).origin && !u.pathname.startsWith('/callback') && !u.pathname.startsWith('/auth'), { timeout: 20000 })
  if (pathname) await page.waitForURL((u) => u.pathname === pathname, { timeout: 20000 })
  await settle(page)
}

async function otpLogin(page, secret) {
  await page.waitForSelector('#otp')
  await page.fill('#otp', await totp(secret))
  await Promise.all([page.waitForNavigation(), page.click('#kc-otp-login-form button[type=submit]')])
}

async function main() {
  const email = `e2e-${RUN}@openksef.invalid`
  const userId = await createUser(email)
  let secret
  let { ctx, page } = await fresh()

  await check('hosted login page is the themed Keycloak form', async () => {
    await page.goto(`${BASE}/invoices`)
    await atKeycloakLogin(page)
    expect((await page.title()).includes('OpenKSeF'), `title: ${await page.title()}`)
    expect(new URL(page.url()).pathname.startsWith('/auth/realms/openksef/'), page.url())
    await axe(page, 'login')
  }, page)

  await check('login fails with a wrong password and stays on the form', async () => {
    await signIn(page, email, 'zle-haslo-e2e')
    await atKeycloakLogin(page)
    const error = await page.textContent('.ui-form-error')
    expect(/nieprawidłow/i.test(error ?? ''), `error: ${error}`)
    await axe(page, 'login--error')
  }, page)

  await check('login succeeds: forced 2FA setup, recovery codes, back on returnTo or onboarding', async () => {
    await signIn(page, email, PASSWORD)
    secret = await enrol(page, '')
    await inPortal(page, '/onboarding')
  }, page)

  await check('a11y Onboarding', () => axe(page, 'onboarding'), page)

  await check('onboarding with a fake NIP and no token lands on Start', async () => {
    await page.fill('[data-testid="onboarding-nip"]', '1111111111')
    await page.click('[data-testid="onboarding-next"]')
    await page.click('[data-testid="onboarding-skip-token"]')
    await inPortal(page, '/')
  }, page)

  for (const [name, url] of [['dashboard', '/'], ['invoice-list', '/invoices'], ['settings', '/settings']]) {
    await check(`a11y ${name}`, async () => {
      await page.goto(`${BASE}${url}`)
      await inPortal(page, url)
      await axe(page, name)
    }, page)
  }

  await check('a11y AdminSetup', async () => {
    await page.goto(`${BASE}/admin-setup`)
    await axe(page, 'admin-setup')
  }, page)

  await check('logout ends on the themed login form, no confirm page or English error', async () => {
    await page.goto(`${BASE}/`)
    await inPortal(page, '/')
    await page.click('[data-testid="nav-logout"]')
    await atKeycloakLogin(page)
    const text = await page.textContent('body')
    expect(!/Code not valid|Do you want to log out|logout/i.test(text ?? ''), 'confirm page or English error')
    await page.goto(`${BASE}/invoices`)
    await atKeycloakLogin(page)
  }, page)

  await check('2FA challenge: wrong code rejected, right code logs in to returnTo', async () => {
    await signIn(page, email, PASSWORD)
    await page.waitForSelector('#otp')
    await axe(page, 'otp')
    await page.fill('#otp', '000000')
    await Promise.all([page.waitForNavigation(), page.click('#kc-otp-login-form button[type=submit]')])
    await page.waitForSelector('#otp')
    const error = await page.textContent('.ui-form-error')
    expect(/Nieprawidłowy kod/.test(error ?? ''), `error: ${error}`)
    await otpLogin(page, secret)
    await inPortal(page, '/invoices')
  }, page)

  await check('session expiry redirects to hosted login and back to the page', async () => {
    await admin('POST', `/users/${userId}/logout`)
    const expired = await page.evaluate(() => {
      const key = Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'))
      if (!key) return false
      const user = JSON.parse(sessionStorage.getItem(key))
      user.expires_at = Math.floor(Date.now() / 1000) - 60
      sessionStorage.setItem(key, JSON.stringify(user))
      return true
    })
    expect(expired, 'no oidc user in sessionStorage')
    await page.reload()
    await atKeycloakLogin(page)
    await signIn(page, email, PASSWORD)
    await otpLogin(page, secret)
    await inPortal(page, '/invoices')
  }, page)

  await check('throwaway tenant deleted through the API', async () => {
    const left = await page.evaluate(async () => {
      const key = Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'))
      const headers = { Authorization: `Bearer ${JSON.parse(sessionStorage.getItem(key)).access_token}` }
      for (const t of await (await fetch('/api/tenants', { headers })).json()) await fetch(`/api/tenants/${t.id}`, { method: 'DELETE', headers })
      return (await (await fetch('/api/tenants', { headers })).json()).length
    })
    expect(left === 0, `${left} tenants left`)
  }, page)

  await check('API 401 after Keycloak logout ends the session on hosted login', async () => {
    await admin('POST', `/users/${userId}/logout`)
    await page.evaluate(() => {
      const key = Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'))
      const user = JSON.parse(sessionStorage.getItem(key))
      user.access_token = `${user.access_token}x`
      sessionStorage.setItem(key, JSON.stringify(user))
    })
    await page.reload()
    await atKeycloakLogin(page)
  }, page)
  await ctx.close()

  const realm = await admin('GET', '')
  ;({ ctx, page } = await fresh())
  await check(`register ${realm.registrationAllowed ? 'creates an account and forces 2FA setup' : 'link hidden when registration is off'}`, async () => {
    await page.goto(`${BASE}/`)
    await atKeycloakLogin(page)
    const link = page.locator('a:has-text("Zarejestruj się")')
    if (!realm.registrationAllowed) {
      expect((await link.count()) === 0, 'register link shown with registration off')
      return
    }
    await Promise.all([page.waitForNavigation(), link.click()])
    await axe(page, 'register')
    const regEmail = `e2e-reg-${RUN}@openksef.invalid`
    await page.fill('#firstName', 'E2E')
    await page.fill('#lastName', 'Rejestracja')
    await page.fill('#email', regEmail)
    await page.fill('#password', PASSWORD)
    await page.fill('#password-confirm', PASSWORD)
    await Promise.all([page.waitForNavigation(), page.click('.auth-card form button[type=submit]')])
    const [registered] = await admin('GET', `/users?email=${encodeURIComponent(regEmail)}&exact=true`)
    expect(registered, 'user not created')
    created.push(registered.id)
    await enrol(page)
    await inPortal(page, '/onboarding')
  }, page)
  await ctx.close()

  const dataUser = process.env.DATA_USER
  if (!dataUser) {
    results.push({ name: 'a11y with invoices (Dashboard, InvoiceList, InvoiceDetails)', ok: true, skipped: 'DATA_USER not set' })
    return
  }
  const [data] = await admin('GET', `/users?username=${encodeURIComponent(dataUser)}&exact=true`)
  expect(data, 'DATA_USER not found')
  expect((await admin('GET', `/users/${data.id}/credentials`)).every((c) => c.type === 'password'), 'DATA_USER must be password-only')
  ;({ ctx, page } = await fresh())
  try {
    await check('data user logs in (2FA setup)', async () => {
      await page.goto(`${BASE}/`)
      await atKeycloakLogin(page)
      await signIn(page, dataUser, process.env.DATA_PASSWORD)
      await enrol(page)
      await inPortal(page, '/')
    }, page)
    await check('a11y dashboard with invoices', () => axe(page, 'dashboard--data'), page)
    await check('a11y invoice-list with invoices', async () => {
      await page.goto(`${BASE}/invoices`)
      await inPortal(page, '/invoices')
      await page.waitForSelector('[data-testid="invoice-row"]')
      await axe(page, 'invoice-list--data')
    }, page)
    await check('a11y invoice-details', async () => {
      await page.click('[data-testid="invoice-row"] a')
      await page.waitForURL(/\/invoices\/.+/)
      await page.waitForSelector('[data-testid="invoice-detail-ksef-number"]')
      await axe(page, 'invoice-details')
    }, page)
  } finally {
    await ctx.close()
    await restoreToPasswordOnly(data.id)
  }
}

let exitCode = 1
try {
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'chrome', headless: true })
  await main()
  const failed = results.filter((r) => !r.ok)
  console.log(`\n${results.length} checks, ${failed.length} failed`)
  for (const s of results.filter((r) => r.skipped)) console.log(`SKIP ${s.name}: ${s.skipped}`)
  for (const v of violations) console.log(`axe ${v.known ? `known ${v.known}` : 'NEW'} ${v.page}/${v.scheme} ${v.impact} ${v.id} x${v.count} ${v.targets.join(' | ')}`)
  exitCode = failed.length ? 1 : 0
} catch (error) {
  console.log(`ERROR ${error.stack}`)
} finally {
  await browser?.close()
  for (const id of created) await admin('DELETE', `/users/${id}`).catch((e) => console.log(`cleanup ${id}: ${e.message}`))
}
process.exit(exitCode)
