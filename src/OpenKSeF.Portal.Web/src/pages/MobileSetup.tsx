import type { ReactElement } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { Link } from 'react-router-dom'

function buildQrPayload(serverUrl: string): string {
  return JSON.stringify({
    type: 'openksef-setup',
    version: 1,
    serverUrl,
  })
}

export function MobileSetupPage(): ReactElement {
  const serverUrl = globalThis.location?.origin ?? 'http://localhost:8080'

  return (
    <section>
      <header className="page-header">
        <h1>Aplikacja mobilna</h1>
        <Link to="/devices" data-testid="mobile-setup-devices-link">
          Urządzenia
        </Link>
      </header>

      <div className="mobile-setup-container">
        <div className="mobile-setup-card">
          <h2 className="mobile-setup-heading">Połącz aplikację mobilną</h2>
          <p className="mobile-setup-description">
            Zeskanuj poniższy kod QR w aplikacji OpenKSeF Mobile, aby ustawić adres serwera. Potem
            zaloguj się w aplikacji.
          </p>

          <div className="mobile-setup-qr-wrapper" data-testid="mobile-setup-qr">
            <QRCodeSVG value={buildQrPayload(serverUrl)} size={280} level="M" marginSize={4} />
          </div>

          <div className="mobile-setup-server-info">
            <span className="mobile-setup-server-label">Adres serwera:</span>
            <code className="mobile-setup-server-url">{serverUrl}</code>
          </div>

          <div className="mobile-setup-instructions">
            <h3>Instrukcja</h3>
            <ol>
              <li>Zainstaluj aplikację OpenKSeF Mobile na telefonie</li>
              <li>
                Na ekranie logowania w aplikacji wybierz przycisk „QR” (jeśli go nie widać, najpierw
                dotknij „Zmień serwer” lub „Serwer”)
              </li>
              <li>Zeskanuj powyższy kod -- adres serwera zostanie ustawiony, a aplikacja wróci do ekranu logowania</li>
            </ol>
          </div>
        </div>
      </div>
    </section>
  )
}
