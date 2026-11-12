import { readFileSync, readdirSync, writeFileSync } from 'node:fs'

const file = 'public/THIRD-PARTY-NOTICES.txt'
const separator = `\n${'-'.repeat(72)}\n`
const partBreak = /\n={72}\n/
const lock = JSON.parse(readFileSync('package-lock.json', 'utf8')).packages
const raw = readFileSync(file, 'utf8')
const eol = raw.includes('\r\n') ? '\r\n' : '\n'

const licenceText = (name) => {
  const dir = `node_modules/${name}`
  const licence = readdirSync(dir).find((entry) => /^licen[cs]e/i.test(entry))
  return licence && readFileSync(`${dir}/${licence}`, 'utf8').replace(/\r\n/g, '\n').trim()
}

const entries = raw.replace(/\r\n/g, '\n').split(separator).map((entry) => {
  const match = entry.match(/^(\S+) \d\S*(\n(License: [^\n]*)\n\n)([\s\S]*)$/)
  const pkg = match && lock[`node_modules/${match[1]}`]
  if (!pkg || pkg.dev) return entry
  if (pkg.license && !match[3].startsWith(`License: ${pkg.license}`)) throw new Error(`${match[1]}: licence is now ${pkg.license}`)
  const text = licenceText(match[1])
  if (!text) return entry.replace(/^\S+ \d\S*/, `${match[1]} ${pkg.version}`)
  const tail = match[4].match(partBreak)
  return `${match[1]} ${pkg.version}${match[2]}${text}\n${tail ? match[4].slice(tail.index) : ''}`
})

writeFileSync(file, entries.join(separator).replace(/\n/g, eol))
