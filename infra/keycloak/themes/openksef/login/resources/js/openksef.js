document.addEventListener('submit', (event) => {
  const button = event.submitter ?? event.target.querySelector('button[data-pending]')
  if (!button?.dataset.pending) return
  button.textContent = button.dataset.pending
  setTimeout(() => { button.disabled = true })
})

document.addEventListener('change', (event) => {
  const target = event.target.closest('[data-enables]')
  if (target) document.getElementById(target.dataset.enables).disabled = !target.checked
})

function codesText() {
  return [...document.querySelectorAll('.auth-codes li')].map((li, i) => `${i + 1}. ${li.textContent.trim()}`).join('\n')
}

function flash(button, text) {
  const label = button.querySelector('span')
  const original = label.textContent
  label.textContent = text
  setTimeout(() => { label.textContent = original }, 2000)
}

// Keycloak gives login pages no sign of the device grant, so the code page marks the tab for the pairing hint.
const pairingNotice = document.querySelector('[data-pairing-notice]')
if (pairingNotice && sessionStorage.getItem('openksef-pairing')) pairingNotice.hidden = false
else if (!document.getElementById('kc-form-login')) sessionStorage.removeItem('openksef-pairing')
document.addEventListener('submit', (event) => {
  if (event.target.id === 'kc-user-verify-device-user-code-form') sessionStorage.setItem('openksef-pairing', '1')
})

const resentNotice = document.querySelector('[data-resent-notice]')
if (resentNotice && sessionStorage.getItem('openksef-resent')) {
  sessionStorage.removeItem('openksef-resent')
  resentNotice.hidden = false
}

document.addEventListener('click', (event) => {
  if (event.target.closest('[data-resend]')) sessionStorage.setItem('openksef-resent', '1')
  const button = event.target.closest('[data-action]')
  if (!button) return
  const { action } = button.dataset
  if (action === 'copy-secret') {
    void navigator.clipboard.writeText(document.querySelector('.auth-secret span').textContent.replace(/\s/g, '')).then(() => flash(button, 'Skopiowano'))
  } else if (action === 'copy-codes') {
    void navigator.clipboard.writeText(codesText()).then(() => flash(button, 'Skopiowano'))
  } else if (action === 'download-codes') {
    const email = document.querySelector('.auth-print-email')?.textContent ?? ''
    const text = `Kody zapasowe OpenKSeF\n${email}\n${new Date().toLocaleString('pl-PL')}\n\n${codesText()}\n`
    const link = document.createElement('a')
    link.href = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))
    link.download = 'openksef-kody-zapasowe.txt'
    link.click()
    URL.revokeObjectURL(link.href)
  } else if (action === 'print-codes') {
    window.print()
  }
})
