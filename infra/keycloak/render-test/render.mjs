// Render test for the openksef Keycloak theme (ADR-003 decision 10).
// usage: node infra/keycloak/render-test/render.mjs <image> [outDir] [filter]
// env: BROWSER_CHANNEL (msedge | chrome, default chrome), SHOT_PREFIX, REF_DIR (designer shots, adds pixel diffs to the report)
import { execFileSync } from 'node:child_process'
import { createHmac } from 'node:crypto'
import { createRequire } from 'node:module'
import fs from 'node:fs'
import http from 'node:http'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPO = path.resolve(HERE, '../../..')
const PORTAL = path.join(REPO, 'src/OpenKSeF.Portal.Web')
const THEME = path.join(REPO, 'infra/keycloak/themes/openksef/login')
const require = createRequire(path.join(PORTAL, 'package.json'))
const { chromium } = require('playwright-core')
const axeSrc = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8')

const [IMAGE, OUT = path.join(HERE, 'out'), FILTER] = process.argv.slice(2)
if (!IMAGE) throw new Error('usage: render.mjs <image> [outDir] [filter]')
const PREFIX = process.env.SHOT_PREFIX ?? ''
const REF_DIR = process.env.REF_DIR
const NAME = `okt-render-${process.pid}`
const EMAIL = 'anna@firma-testowa.pl'
const PASSWORD = 'Haslo1234!'

const NO_MOCK = new Set(['update-profile'])
const KNOWN = {
  'update-password--errors': 'Keycloak reports one password error at a time',
  'update-password--required': 'an admin-required change and a reset link render the same page, so no admin notice',
  'verify-email': 'link lifetime sentence dropped: login pages do not get the realm value',
  'verify-email--resent': 'link lifetime sentence dropped: login pages do not get the realm value',
  'device-consent': 'the consent page gets no user, so no "Konto {email}" line',
  'error--idp': 'Keycloak shows a Google failure as an error on the login form, not as an error page',
  terms: 'terms text comes from the realm (termsText), the mock shows a sample text',
}
const SKIP_FTL = new Set(['template.ftl'])

const docker = (...args) => execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

function cleanup() {
  for (const c of [`${NAME}-kc`, `${NAME}-mail`]) { try { docker('rm', '-f', c) } catch { /* not started */ } }
  try { docker('network', 'rm', NAME) } catch { /* not created */ }
}

function startStack() {
  docker('network', 'create', NAME)
  docker('run', '-d', '--name', `${NAME}-mail`, '--network', NAME, '--network-alias', 'mailpit', '-p', '127.0.0.1::8025', 'axllent/mailpit:v1.30.0')
  docker('run', '-d', '--name', `${NAME}-kc`, '--network', NAME, '-p', '127.0.0.1::8080',
    '-e', 'KC_BOOTSTRAP_ADMIN_USERNAME=admin', '-e', 'KC_BOOTSTRAP_ADMIN_PASSWORD=admin', '-e', 'KC_HTTP_RELATIVE_PATH=/auth',
    IMAGE, 'start-dev')
  const port = (c, p) => docker('port', c, p).split('\n')[0].split(':').pop()
  return { kc: `http://localhost:${port(`${NAME}-kc`, '8080/tcp')}/auth`, mail: `http://127.0.0.1:${port(`${NAME}-mail`, '8025/tcp')}` }
}

let KC, MAIL, adminToken
async function api(method, url, body) {
  await login()
  const res = await fetch(`${KC}/admin/realms${url}`, {
    method,
    headers: { Authorization: `Bearer ${adminToken}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) throw new Error(`${method} ${url}: ${res.status} ${await res.text()}`)
  const text = await res.text()
  return text ? JSON.parse(text) : res.headers.get('location')
}

async function waitForKeycloak() {
  for (let i = 0; i < 120; i++) {
    try { if ((await fetch(`${KC}/realms/master`)).ok) return } catch { /* starting */ }
    await sleep(1000)
  }
  throw new Error('Keycloak did not start')
}

async function login() {
  const res = await fetch(`${KC}/realms/master/protocol/openid-connect/token`, {
    method: 'POST',
    body: new URLSearchParams({ grant_type: 'password', client_id: 'admin-cli', username: 'admin', password: 'admin' }),
  })
  adminToken = (await res.json()).access_token
}

async function createUser(realm, user) {
  const location = await api('POST', `/${realm}/users`, { enabled: true, emailVerified: true, ...user })
  return location.split('/').pop()
}

async function seed() {
  await api('POST', '', { realm: 'fakegoogle', enabled: true })
  const verifyProfile = (await api('GET', '/fakegoogle/authentication/required-actions')).find((a) => a.alias === 'VERIFY_PROFILE')
  await api('PUT', '/fakegoogle/authentication/required-actions/VERIFY_PROFILE', { ...verifyProfile, enabled: false })
  await api('POST', '/fakegoogle/clients', { clientId: 'fake-google', secret: 'broker-secret', publicClient: false, redirectUris: ['*'], standardFlowEnabled: true })
  await createUser('fakegoogle', { username: 'g-link', email: EMAIL, firstName: 'Anna', lastName: 'Testowa', credentials: [{ type: 'password', value: PASSWORD, temporary: false }] })
  await createUser('fakegoogle', { username: 'g-new', email: 'nowa@firma-testowa.pl', firstName: 'Anna', credentials: [{ type: 'password', value: PASSWORD, temporary: false }] })

  await api('POST', '', {
    realm: 'openksef',
    enabled: true,
    displayName: 'OpenKSeF',
    loginTheme: 'openksef',
    accountTheme: 'openksef',
    internationalizationEnabled: true,
    supportedLocales: ['pl'],
    defaultLocale: 'pl',
    registrationAllowed: true,
    resetPasswordAllowed: true,
    loginWithEmailAllowed: true,
    duplicateEmailsAllowed: false,
    passwordPolicy: 'length(8)',
    smtpServer: { host: 'mailpit', port: '1025', from: 'noreply@openksef.local', fromDisplayName: 'OpenKSeF' },
  })
  const realm = '/openksef'
  await api('POST', `${realm}/clients`, {
    clientId: 'openksef-portal-web', publicClient: true, standardFlowEnabled: true, redirectUris: ['*'], webOrigins: ['+'],
    frontchannelLogout: true, attributes: { 'frontchannel.logout.url': `${KC}/cb`, 'post.logout.redirect.uris': '+' },
  })
  await api('POST', `${realm}/clients`, {
    clientId: 'openksef-mobile', name: 'Aplikacja mobilna OpenKSeF', publicClient: true, standardFlowEnabled: true, redirectUris: ['*'],
    attributes: { 'oauth2.device.authorization.grant.enabled': 'true' },
  })
  await api('POST', `${realm}/clients`, {
    clientId: 'device-expiry', publicClient: true, standardFlowEnabled: false, redirectUris: ['*'],
    attributes: { 'oauth2.device.authorization.grant.enabled': 'true', 'oauth2.device.code.lifespan': '1' },
  })
  await api('POST', `${realm}/identity-provider/instances`, {
    alias: 'google', displayName: 'Google', providerId: 'oidc', enabled: true, trustEmail: true,
    config: {
      clientId: 'fake-google', clientSecret: 'broker-secret', clientAuthMethod: 'client_secret_post', syncMode: 'IMPORT', defaultScope: 'openid email profile',
      authorizationUrl: `${KC}/realms/fakegoogle/protocol/openid-connect/auth`,
      tokenUrl: 'http://localhost:8080/auth/realms/fakegoogle/protocol/openid-connect/token',
      disableUserInfo: 'true',
      validateSignature: 'false', pkceEnabled: 'false',
    },
  })

  for (const alias of ['CONFIGURE_RECOVERY_AUTHN_CODES', 'TERMS_AND_CONDITIONS']) {
    const action = (await api('GET', `${realm}/authentication/required-actions`)).find((a) => a.alias === alias)
    if (action) await api('PUT', `${realm}/authentication/required-actions/${alias}`, { ...action, enabled: true })
    else await api('POST', `${realm}/authentication/register-required-action`, { providerId: alias, name: alias })
  }
  for (const execution of await api('GET', `${realm}/authentication/flows/browser/executions`)) {
    if (execution.providerId === 'auth-recovery-authn-code-form') await api('PUT', `${realm}/authentication/flows/browser/executions`, { ...execution, requirement: 'ALTERNATIVE' })
  }

  const users = {}
  const add = async (key, email, extra = {}) => {
    users[key] = await createUser('openksef', { username: email, email, firstName: 'Anna', lastName: 'Testowa', credentials: [{ type: 'password', value: PASSWORD, temporary: false }], ...extra })
  }
  await add('anna', EMAIL)
  await add('otp', 'anka@firma-testowa.pl', { requiredActions: ['CONFIGURE_TOTP', 'CONFIGURE_RECOVERY_AUTHN_CODES'] })
  await add('required', 'ania@firma-testowa.pl', { requiredActions: ['UPDATE_PASSWORD'] })
  await add('verify', 'asia@firma-testowa.pl', { emailVerified: false, requiredActions: ['VERIFY_EMAIL'] })
  await add('terms', 'olga@firma-testowa.pl', { requiredActions: ['TERMS_AND_CONDITIONS'] })
  await add('profile', 'jola@firma-testowa.pl', { requiredActions: ['UPDATE_PROFILE'] })
  await add('info', 'lena@firma-testowa.pl')
  await add('disabled', 'nina@firma-testowa.pl')
  return users
}

async function mails(to) {
  const list = await (await fetch(`${MAIL}/api/v1/search?query=${encodeURIComponent(`to:${to}`)}`)).json()
  return list.messages ?? []
}

async function lastLink(to) {
  for (let i = 0; i < 30; i++) {
    const [latest] = await mails(to)
    if (latest) {
      const body = await (await fetch(`${MAIL}/api/v1/message/${latest.ID}`)).json()
      const link = (body.Text ?? '').match(/https?:\/\/\S+action-token\S+/)?.[0]
      if (link) {
        await fetch(`${MAIL}/api/v1/messages`, { method: 'DELETE', body: JSON.stringify({ IDs: [latest.ID] }) })
        return link
      }
    }
    await sleep(500)
  }
  throw new Error(`no mail for ${to}`)
}

function totp(secret) {
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)))
  const h = createHmac('sha1', Buffer.from(secret)).update(counter).digest()
  const o = h[h.length - 1] & 15
  return String(((h.readUInt32BE(o) & 0x7fffffff) % 1000000)).padStart(6, '0')
}

const authUrl = (extra = {}, client = 'openksef-portal-web') => `${KC}/realms/openksef/protocol/openid-connect/auth?${new URLSearchParams({
  client_id: client, redirect_uri: `${KC}/cb`, response_type: 'code', scope: 'openid', ...extra,
})}`

function mockServer() {
  const map = (p) => {
    if (p === '/mock.html') return path.join(HERE, 'mock.html')
    if (p === '/assets/ui.css') return path.join(PORTAL, 'src/components/ui.css')
    if (p === '/assets/auth.css') return path.join(PORTAL, 'src/components/auth.css')
    if (p === '/assets/index.css') return path.join(PORTAL, 'src/index.css')
    if (p === '/assets/icons.svg') return path.join(PORTAL, 'public/icons.svg')
    if (p.includes('/fonts/')) return path.join(PORTAL, 'src/assets/fonts', path.basename(p))
    return null
  }
  const types = { '.html': 'text/html', '.css': 'text/css', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' }
  return http.createServer((req, res) => {
    const f = map(decodeURIComponent(new URL(req.url, 'http://x').pathname))
    if (!f || !fs.existsSync(f)) { res.writeHead(404); return res.end() }
    res.writeHead(200, { 'Content-Type': types[path.extname(f)] ?? 'application/octet-stream' })
    fs.createReadStream(f).pipe(res)
  })
}

const KEY_BOXES = {
  logo: '.auth-logo',
  wordmark: '.auth-card__title',
  heading: '.auth-card__heading',
  input: '.auth-card input:not([type=hidden]):not([type=checkbox]):not([hidden])',
  primary: '.auth-card .ui-button--primary',
}
const ENGLISH = /\b(the|and|your|you|password|username|sign in|log in|login|submit|cancel|back to|required|continue|try another|another way|click here|account|address|validation|verify|expired|invalid|error|code|confirm|welcome|powered)\b/i

async function inspect(page) {
  return page.evaluate(({ KEY_BOXES, englishSource }) => {
    const english = new RegExp(englishSource, 'i')
    const box = (sel) => {
      const el = [...document.querySelectorAll(sel)].find((e) => e.getClientRects().length)
      if (!el) return null
      const r = el.getBoundingClientRect()
      return { y: Math.round(r.top + scrollY), h: Math.round(r.height), x: Math.round(r.left), w: Math.round(r.width) }
    }
    const boxes = Object.fromEntries(Object.entries(KEY_BOXES).map(([k, s]) => [k, box(s)]))
    const texts = [document.title, document.body.innerText,
      ...[...document.querySelectorAll('[placeholder],[aria-label],[alt],[title],input[type=submit]')].map((e) => [e.placeholder, e.getAttribute('aria-label'), e.alt, e.title, e.value].filter(Boolean).join(' '))].join('\n')
    const allowed = texts.replace(/Google|Microsoft Authenticator|Aegis|OpenKSeF|KSeF|Email/g, '')
    const css = getComputedStyle(document.documentElement)
    const body = getComputedStyle(document.body)
    const probe = document.createElement('div')
    probe.style.color = css.getPropertyValue('--ui-bg')
    document.body.append(probe)
    const bgToken = getComputedStyle(probe).color
    probe.remove()
    const inputs = [...document.querySelectorAll('input:not([type=hidden]):not([type=checkbox])')].filter((e) => e.getClientRects().length)
    return {
      boxes,
      pageId: document.body.dataset.pageId ?? null,
      english: allowed.match(english)?.[0] ?? null,
      keycloak: /keycloak/i.test(document.documentElement.outerHTML),
      font: body.fontFamily.split(',')[0].replace(/['"]/g, '').trim(),
      bgOk: body.backgroundColor === bgToken,
      hScroll: document.documentElement.scrollWidth > innerWidth,
      smallInputs: innerWidth <= 820 ? inputs.filter((e) => parseFloat(getComputedStyle(e).fontSize) < 16).length : 0,
      lang: document.documentElement.lang,
    }
  }, { KEY_BOXES, englishSource: ENGLISH.source })
}

async function pixelDiff(browser, a, b) {
  const page = await browser.newPage()
  const toUrl = (f) => `data:image/png;base64,${fs.readFileSync(f).toString('base64')}`
  const result = await page.evaluate(async ([ua, ub]) => {
    const load = (u) => new Promise((r) => { const i = new Image(); i.onload = () => r(i); i.src = u })
    const [ia, ib] = await Promise.all([load(ua), load(ub)])
    const w = Math.max(ia.width, ib.width), h = Math.max(ia.height, ib.height)
    const data = (img) => { const c = new OffscreenCanvas(w, h); const x = c.getContext('2d'); x.fillStyle = '#f0f'; x.fillRect(0, 0, w, h); x.drawImage(img, 0, 0); return x.getImageData(0, 0, w, h).data }
    const da = data(ia), db = data(ib)
    let diff = 0
    for (let i = 0; i < da.length; i += 4) if (Math.abs(da[i] - db[i]) + Math.abs(da[i + 1] - db[i + 1]) + Math.abs(da[i + 2] - db[i + 2]) > 48) diff++
    return { diff, total: w * h, size: [ia.width, ia.height, ib.width, ib.height] }
  }, [toUrl(a), toUrl(b)])
  await page.close()
  return result
}

const report = []
const seenPages = new Set()
let browser, mockBase

async function capture(page, name, first = false) {
  if (FILTER && !name.includes(FILTER)) return
  await page.waitForLoadState('load')
  await page.addStyleTag({ content: '*{animation:none!important;transition:none!important;caret-color:transparent!important}' })
  const combos = first ? [['light', 1440], ['dark', 1440], ['light', 390], ['dark', 390]] : [['light', 1440], ['dark', 390]]
  const [p, s] = name.split('--')
  for (const [theme, w] of combos) {
    const viewport = { width: w, height: w === 390 ? 844 : 900 }
    await page.setViewportSize(viewport)
    await page.emulateMedia({ colorScheme: theme, reducedMotion: 'reduce' })
    await page.evaluate(() => document.fonts.ready)
    const file = path.join(OUT, `${PREFIX}${name}_${theme}_${w}.png`)
    await page.screenshot({ path: file, fullPage: true })
    const live = await inspect(page)
    await page.addScriptTag({ content: axeSrc })
    const axe = await page.evaluate(async () => (await window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map((v) => `${v.id}(${v.nodes.length})`))
    if (live.pageId) seenPages.add(live.pageId.replace(/^login-/, ''))

    const entry = { name, theme, w, file: path.basename(file), pageId: live.pageId, findings: [] }
    const f = entry.findings
    if (live.english) f.push(`english:${live.english}`)
    if (live.keycloak) f.push('keycloak-string')
    if (live.font !== 'Inter') f.push(`font:${live.font}`)
    if (!live.bgOk) f.push('bg-token')
    if (live.hScroll) f.push('h-scroll')
    if (live.smallInputs) f.push(`input<16px:${live.smallInputs}`)
    if (live.lang !== 'pl') f.push(`lang:${live.lang}`)
    if (axe.length) f.push(`axe:${axe.join(' ')}`)

    if (!NO_MOCK.has(p)) {
      const ctx = await browser.newContext({ viewport, colorScheme: theme, reducedMotion: 'reduce', deviceScaleFactor: 1 })
      const mock = await ctx.newPage()
      await mock.goto(`${mockBase}?p=${p}&s=${s ?? 'default'}`)
      await mock.waitForSelector('body[data-ready]')
      await mock.addStyleTag({ content: '*{animation:none!important;transition:none!important}' })
      await mock.evaluate(() => document.fonts.ready)
      const ref = await inspect(mock)
      await ctx.close()
      entry.boxes = {}
      for (const k of Object.keys(KEY_BOXES)) {
        const a = live.boxes[k], b = ref.boxes[k]
        entry.boxes[k] = a && b ? { dy: a.y - b.y, dh: a.h - b.h } : { live: !!a, ref: !!b }
        if (!!a !== !!b) f.push(`box-missing:${k}(live=${!!a},ref=${!!b})`)
        else if (a && (Math.abs(a.y - b.y) > 4 || Math.abs(a.h - b.h) > 4)) f.push(`box:${k} dy=${a.y - b.y} dh=${a.h - b.h}`)
      }
    }
    if (KNOWN[name]) {
      entry.known = { reason: KNOWN[name], findings: f.filter((x) => x.startsWith('box')) }
      entry.findings = f.filter((x) => !x.startsWith('box'))
    }
    if (REF_DIR) {
      const ref = path.join(REF_DIR, `${name}_${theme}_${w}.png`)
      if (fs.existsSync(ref)) entry.pixels = await pixelDiff(browser, file, ref)
    }
    report.push(entry)
    const shown = entry.findings
    console.log(`${shown.length ? 'FAIL' : entry.known ? 'known' : 'ok  '} ${entry.file}${shown.length ? ' ' + shown.join(' ') : ''}`)
  }
}

async function fresh() {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1, locale: 'pl-PL' })
  await ctx.route(`${KC}/cb**`, (r) => r.fulfill({ contentType: 'text/html', body: '<!doctype html><html lang="pl"><title>OK</title><p>OK</p></html>' }))
  return { ctx, page: await ctx.newPage() }
}

async function signIn(page, email, password = PASSWORD) {
  await page.fill('#username', email)
  await page.fill('#password', password)
  await page.click('#kc-form-login button[type=submit]')
}

async function submit(page, selector = '.auth-card form button[type=submit]') {
  await Promise.all([page.waitForNavigation(), page.click(selector)])
}

async function run(users) {
  let { ctx, page } = await fresh()

  await page.goto(authUrl())
  await capture(page, 'login', true)
  await signIn(page, EMAIL, 'zle-haslo')
  await page.waitForLoadState('load')
  await capture(page, 'login--error')

  await page.click('text=Nie pamiętasz hasła?')
  await capture(page, 'reset', true)
  await page.fill('#username', EMAIL)
  await submit(page)
  await capture(page, 'login--reset-sent')

  await page.click('text=Zarejestruj się')
  await capture(page, 'register', true)
  await page.fill('#firstName', 'Anna')
  await page.fill('#lastName', 'Testowa')
  await page.fill('#email', EMAIL)
  await page.fill('#password', 'xxxx')
  await page.fill('#password-confirm', 'xxxxx')
  await submit(page)
  await capture(page, 'register--errors')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(await lastLink(EMAIL))
  await capture(page, 'update-password', true)
  await page.fill('#password-new', 'Nowe1234!')
  await page.fill('#password-confirm', 'Inne1234!')
  await submit(page)
  await capture(page, 'update-password--errors')
  await page.fill('#password-new', PASSWORD)
  await page.fill('#password-confirm', PASSWORD)
  await page.evaluate(() => { document.querySelector('#logout-sessions').checked = false })
  await submit(page)
  if (await page.locator('.auth-card').count()) await capture(page, 'email-result--password')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await page.fill('#username', EMAIL)
  await page.fill('#password', PASSWORD)
  await page.evaluate(() => document.querySelector('#kc-form-login').addEventListener('submit', (e) => e.preventDefault()))
  await page.click('#kc-form-login button[type=submit]')
  await page.waitForFunction(() => document.querySelector('#kc-form-login button').textContent.includes('Logowanie...'))
  await capture(page, 'login--submitting')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, EMAIL)
  await page.waitForURL(`${KC}/cb**`)
  await page.goto(authUrl({ prompt: 'login' }))
  await capture(page, 'login--reauth')
  await page.goto(authUrl({ kc_action: 'UPDATE_PASSWORD' }))
  await capture(page, 'update-password--aia')
  await page.goto(`${KC}/realms/openksef/protocol/openid-connect/logout`)
  await capture(page, 'logout-confirm', true)
  await submit(page)
  await capture(page, 'logging-out', true)
  await ctx.close()

  const idp = await api('GET', '/openksef/identity-provider/instances/google')
  await api('PUT', '/openksef/identity-provider/instances/google', { ...idp, enabled: false })
  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await capture(page, 'login--no-google')
  await ctx.close()
  await api('PUT', '/openksef/identity-provider/instances/google', { ...idp, enabled: true })

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'ania@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'update-password--required')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'asia@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'verify-email', true)
  await lastLink('asia@firma-testowa.pl')
  await sleep(31000)
  await page.click('text=Wyślij link ponownie')
  await page.waitForLoadState('load')
  await capture(page, 'verify-email--resent')
  const verifyLink = await lastLink('asia@firma-testowa.pl')
  await ctx.close()
  ;({ ctx, page } = await fresh())
  await page.goto(verifyLink)
  await capture(page, 'email-result--confirm')
  await submit(page, '.auth-card .ui-button--primary').catch(() => page.click('.auth-card .ui-button--primary'))
  await page.waitForLoadState('load')
  await capture(page, 'email-result', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'anka@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'otp-setup', true)
  const secret = await page.inputValue('input[name=totpSecret]')
  await page.fill('#totp', '000000')
  await submit(page)
  await capture(page, 'otp-setup--error')
  await page.fill('#totp', totp(secret))
  await submit(page)
  await capture(page, 'recovery-setup', true)
  await page.check('#kcRecoveryCodesConfirmationCheck')
  await capture(page, 'recovery-setup--checked')
  await submit(page)
  await page.waitForURL(`${KC}/cb**`)
  await page.goto(authUrl({ kc_action: 'CONFIGURE_TOTP' }))
  await capture(page, 'otp-setup--aia')
  await page.fill('#totp', totp(await page.inputValue('input[name=totpSecret]')))
  await submit(page)
  await page.waitForURL(`${KC}/cb**`)
  const otps = (await api('GET', `/openksef/users/${users.otp}/credentials`)).filter((c) => c.type === 'otp').sort((a, b) => a.createdDate - b.createdDate)
  if (otps.length !== 2 || otps[0].userLabel === otps[1].userLabel) report.push({ name: 'otp-reenrol', findings: [`expected 2 OTPs with distinct labels, got ${JSON.stringify(otps.map((c) => c.userLabel))}`] })
  for (const c of (await api('GET', `/openksef/users/${users.otp}/credentials`)).filter((c) => c.id === otps[0]?.id || c.type === 'recovery-authn-codes')) {
    await api('DELETE', `/openksef/users/${users.otp}/credentials/${c.id}`)
  }
  await page.goto(authUrl({ kc_action: 'CONFIGURE_RECOVERY_AUTHN_CODES' }))
  await page.check('#kcRecoveryCodesConfirmationCheck')
  await submit(page)
  await page.waitForURL(`${KC}/cb**`)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'anka@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'otp', true)
  await page.fill('#otp', '000000')
  await submit(page)
  await capture(page, 'otp--error')
  await submit(page, '#try-another-way')
  await capture(page, 'select-auth', true)
  await submit(page, '.auth-choice button:has-text("Kod zapasowy")')
  await capture(page, 'recovery-use', true)
  await page.fill('#recoveryCodeInput', 'AAAA-BBBB-CCCC')
  await submit(page)
  await capture(page, 'recovery-use--error')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  const device = await (await fetch(`${KC}/realms/openksef/protocol/openid-connect/auth/device`, { method: 'POST', body: new URLSearchParams({ client_id: 'openksef-mobile', scope: 'openid' }) })).json()
  await page.goto(device.verification_uri)
  await capture(page, 'device-code', true)
  await page.fill('#device-user-code', 'AAAA-BBBB')
  await submit(page)
  await capture(page, 'device-code--invalid')
  const expired = await (await fetch(`${KC}/realms/openksef/protocol/openid-connect/auth/device`, { method: 'POST', body: new URLSearchParams({ client_id: 'device-expiry' }) })).json()
  await sleep(2500)
  await page.fill('#device-user-code', expired.user_code)
  await submit(page)
  await capture(page, 'device-code--expired')
  await page.fill('#device-user-code', device.user_code)
  await submit(page)
  await capture(page, 'login--device')
  await signIn(page, EMAIL)
  await page.waitForLoadState('load')
  await capture(page, 'device-consent', true)
  await submit(page, 'button[name=accept]')
  await capture(page, 'device-ok', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  const denied = await (await fetch(`${KC}/realms/openksef/protocol/openid-connect/auth/device`, { method: 'POST', body: new URLSearchParams({ client_id: 'openksef-mobile', scope: 'openid' }) })).json()
  await page.goto(denied.verification_uri_complete)
  if (await page.locator('#device-user-code').count()) await submit(page)
  await signIn(page, EMAIL)
  await page.waitForLoadState('load')
  await submit(page, 'button[name=cancel]')
  await capture(page, 'device-denied', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await Promise.all([page.waitForNavigation(), page.click('.auth-google-btn')])
  await page.fill('#username', 'g-new')
  await page.fill('#password', PASSWORD)
  await Promise.all([page.waitForURL(/realms\/openksef\//), page.click('[type=submit]')])
  await capture(page, 'idp-review', true)
  await page.fill('#lastName', '')
  await page.evaluate(() => document.querySelector('#lastName').removeAttribute('required'))
  await submit(page)
  await capture(page, 'idp-review--errors')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await Promise.all([page.waitForNavigation(), page.click('.auth-google-btn')])
  await page.fill('#username', 'g-link')
  await page.fill('#password', PASSWORD)
  await Promise.all([page.waitForURL(/realms\/openksef\//), page.click('[type=submit]')])
  await capture(page, 'idp-link', true)
  await submit(page, 'button[name=submitAction]')
  await capture(page, 'idp-link-email', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'olga@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'terms', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  await signIn(page, 'jola@firma-testowa.pl')
  await page.waitForLoadState('load')
  await capture(page, 'update-profile', true)
  await ctx.close()

  await api('PUT', `/openksef/users/${users.info}/execute-actions-email`, ['VERIFY_EMAIL'])
  ;({ ctx, page } = await fresh())
  await page.goto(await lastLink('lena@firma-testowa.pl'))
  await capture(page, 'info', true)
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(`${KC}/realms/openksef/login-actions/action-token?key=nieprawidlowy`)
  await capture(page, 'error', true)
  await page.goto(authUrl({ client_id: 'nieznany-klient' }))
  await capture(page, 'error--generic')
  await page.goto(authUrl())
  await Promise.all([page.waitForNavigation(), page.click('.auth-google-btn')])
  const state = new URL(page.url()).searchParams.get('state')
  await page.goto(`${KC}/realms/openksef/broker/google/endpoint?error=server_error&state=${encodeURIComponent(state)}`)
  await capture(page, 'error--idp')
  await page.goto(authUrl())
  await ctx.clearCookies()
  await signIn(page, EMAIL)
  await page.waitForLoadState('load')
  await capture(page, 'error--cookie')
  await ctx.close()

  ;({ ctx, page } = await fresh())
  await page.goto(authUrl())
  const loginAction = await page.getAttribute('#kc-form-login', 'action')
  await signIn(page, 'anka@firma-testowa.pl')
  await page.waitForLoadState('load')
  await page.goto(loginAction)
  await capture(page, 'error--page-expired')
  await ctx.close()

  await api('PUT', `/openksef/users/${users.disabled}/execute-actions-email`, ['UPDATE_PASSWORD'])
  const disabledLink = await lastLink('nina@firma-testowa.pl')
  await api('PUT', `/openksef/users/${users.disabled}`, { enabled: false })
  ;({ ctx, page } = await fresh())
  await page.goto(disabledLink)
  await capture(page, 'error--disabled')
  await ctx.close()

  const relative = await (await fetch(`${KC}/realms/openksef/account/`)).text()
  const [portal] = await api('GET', '/openksef/clients?clientId=openksef-portal-web')
  await api('PUT', `/openksef/clients/${portal.id}`, { ...portal, baseUrl: 'https://portal.example.test/' })
  const absolute = await (await fetch(`${KC}/realms/openksef/account/`)).text()
  if (!relative.includes('"/settings/security"') || !absolute.includes('"https://portal.example.test/settings/security"'))
    report.push({ name: 'account-redirect', findings: ['account theme does not redirect to the portal client base URL + /settings/security'] })
}

cleanup()
fs.mkdirSync(OUT, { recursive: true })
const server = mockServer()
let exitCode = 1
try {
  ;({ kc: KC, mail: MAIL } = startStack())
  await new Promise((r) => server.listen(0, '127.0.0.1', r))
  mockBase = `http://127.0.0.1:${server.address().port}/mock.html`
  await waitForKeycloak()
  await login()
  const users = await seed()
  browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? 'chrome', headless: true })
  await run(users)
  if (!FILTER) {
    const missing = fs.readdirSync(THEME).filter((f) => f.endsWith('.ftl') && !SKIP_FTL.has(f) && !seenPages.has(f.replace(/\.ftl$/, '')))
    if (missing.length) report.push({ name: 'coverage', findings: missing.map((f) => `no render case: ${f}`) })
  }
  const bad = report.filter((r) => r.findings.length)
  fs.writeFileSync(path.join(OUT, `${PREFIX}render-report.json`), JSON.stringify(report, null, 1))
  console.log(`${report.length} checks, ${bad.length} with findings`)
  for (const b of bad.filter((r) => !r.file)) console.log(`FAIL ${b.name}: ${b.findings.join(', ')}`)
  exitCode = bad.length ? 1 : 0
} finally {
  try { fs.writeFileSync(path.join(OUT, `${PREFIX}keycloak.log`), execFileSync('docker', ['logs', `${NAME}-kc`], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] })) } catch { /* not started */ }
  await browser?.close()
  server.close()
  cleanup()
}
process.exit(exitCode)
