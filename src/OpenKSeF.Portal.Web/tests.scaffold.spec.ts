import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import ts from 'typescript'

const projectRoot = resolve(import.meta.dirname)

function readJson<T>(path: string): T {
  const filePath = resolve(projectRoot, path)
  const content = readFileSync(filePath, 'utf8')
  const parsed = ts.parseConfigFileTextToJson(filePath, content)

  if (parsed.error) {
    throw new Error(`Failed to parse ${path}`)
  }

  return parsed.config as T
}

describe('Third-party notices', () => {
  const notices = readFileSync(resolve(projectRoot, 'public/THIRD-PARTY-NOTICES.txt'), 'utf8')

  it('has the full licence texts for the PDF chunk', () => {
    expect(notices).toContain('SIL OPEN FONT LICENSE Version 1.1')
    expect(notices).toContain('Copyright 2011 The Roboto Project Authors')
    expect(notices).toContain('END OF TERMS AND CONDITIONS')
    for (const name of ['pdfmake 0.3.11', 'i18next 25.8.4', 'xml-js 1.6.11', 'pdfkit 0.19.1', 'fontkit 2.0.4', 'sax 1.6.0']) {
      expect(notices).toContain(`\n${name}\n`)
    }
  })

  it('names every runtime dependency of the portal', () => {
    const { dependencies } = readJson<{ dependencies: Record<string, string> }>('package.json')
    for (const name of Object.keys(dependencies).filter((dep) => dep !== '@tanstack/react-query-devtools')) {
      expect(notices).toMatch(new RegExp(`\\n${name} \\d`))
    }
  })
})

describe('Portal web scaffold requirements', () => {
  it('has required scripts and dependencies', () => {
    const packageJson = readJson<{
      scripts: Record<string, string>
      dependencies: Record<string, string>
    }>('package.json')

    expect(packageJson.scripts.dev).toBeTruthy()
    expect(packageJson.scripts.build).toBeTruthy()
    expect(packageJson.scripts.preview).toBeTruthy()
    expect(packageJson.scripts.test).toBeTruthy()

    expect(packageJson.dependencies['react-router-dom']).toBeTruthy()
    expect(packageJson.dependencies['@tanstack/react-query']).toBeTruthy()
    expect(packageJson.dependencies['oidc-client-ts']).toBeTruthy()
  })

  it('configures strict TypeScript and @ path alias', () => {
    const tsconfig = readJson<{ compilerOptions?: { strict?: boolean } }>('tsconfig.json')
    const tsconfigApp = readJson<{
      compilerOptions?: { baseUrl?: string; paths?: Record<string, string[]> }
    }>('tsconfig.app.json')

    expect(tsconfig.compilerOptions?.strict).toBe(true)
    expect(tsconfigApp.compilerOptions?.baseUrl).toBe('.')
    expect(tsconfigApp.compilerOptions?.paths?.['@/*']).toEqual(['src/*'])
  })

  it('configures Vite proxy and build output', async () => {
    const mod = await import('./vite.config')
    const config = mod.default

    expect(config.build?.outDir).toBe('build')
    expect(config.server?.proxy?.['/api']).toBeTruthy()
    expect(config.server?.proxy?.['/auth']).toBeTruthy()

    const apiProxy = config.server?.proxy?.['/api']
    const authProxy = config.server?.proxy?.['/auth']

    if (typeof apiProxy === 'object' && apiProxy) {
      expect(apiProxy.target).toBe('http://localhost:8080')
    }

    if (typeof authProxy === 'object' && authProxy) {
      expect(authProxy.target).toBe('http://localhost:8080')
    }
  })

  it('provides required environment example values', () => {
    const envExample = readFileSync(resolve(projectRoot, '.env.example'), 'utf8')

    expect(envExample).toContain('VITE_API_BASE_URL=')
    expect(envExample).toContain('VITE_AUTH_AUTHORITY=')
  })
})
