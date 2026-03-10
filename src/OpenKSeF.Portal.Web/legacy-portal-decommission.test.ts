import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const portalWebRoot = resolve(import.meta.dirname)
const repositoryRoot = resolve(portalWebRoot, '..', '..')

function readRepoFile(path: string): string {
  return readFileSync(resolve(repositoryRoot, path), 'utf8')
}

describe('Legacy Blazor portal decommission', () => {
  it('removes legacy portal runtime service from docker compose', () => {
    const compose = readRepoFile('docker-compose.yml')

    expect(compose).not.toMatch(/\n\s{2}portal:\n/)
    expect(compose).not.toContain('profiles: ["portal"]')
    expect(compose).not.toContain('profiles: ["app", "portal"]')
    expect(compose).toContain('portal-web:')
  })

  it('removes legacy portal build and e2e jobs from CI workflow', () => {
    const workflow = readRepoFile('.github/workflows/ci.yml')

    expect(workflow).not.toContain('portal-e2e:')
    expect(workflow).not.toContain('OpenKSeF.Portal/OpenKSeF.Portal.csproj')
    expect(workflow).not.toContain('file: ./src/OpenKSeF.Portal/Dockerfile')
    expect(workflow).not.toContain('openksef-portal:dev')
  })

  it('keeps Blazor portal projects out of default solution build targets', () => {
    const sln = readRepoFile('src/OpenKSeF.sln')

    const projectNames = ['OpenKSeF.Portal', 'OpenKSeF.Portal.Tests', 'OpenKSeF.Portal.E2E']

    for (const projectName of projectNames) {
      const escaped = projectName.replace(/\./g, '\\.')
      const projectLinePattern = new RegExp(`Project\\("\\{[^}]+\\}"\\) = "${escaped}", "[^"]+", "\\{([A-F0-9-]+)\\}"`)
      const match = sln.match(projectLinePattern)
      expect(match, `Expected project ${projectName} to remain in solution`).toBeTruthy()

      const guid = match?.[1]
      expect(sln).not.toContain(`${guid}.Debug|Any CPU.Build.0`)
      expect(sln).not.toContain(`${guid}.Release|Any CPU.Build.0`)
    }
  })
})
