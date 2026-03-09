import { useCallback, useEffect, useMemo, useRef, useState, type ReactElement } from 'react'
import { QRCodeSVG } from 'qrcode.react'

import { generateSetupToken } from '@/api/endpoints/account'
import { Button } from '@/components/Button'

function formatCountdown(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

function buildQrPayload(serverUrl: string, setupToken?: string): string {
  return JSON.stringify({
    type: 'openksef-setup',
    version: 1,
    serverUrl,
    ...(setupToken ? { setupToken } : {}),
  })
}

export function MobileSetupPage(): ReactElement {
  const serverUrl = globalThis.location?.origin ?? 'http://localhost:8080'

  const [setupToken, setSetupToken] = useState<string | null>(null)
  const [secondsLeft, setSecondsLeft] = useState(0)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const fetchToken = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      const res = await generateSetupToken()
      setSetupToken(res.setupToken)
      setSecondsLeft(res.expiresInSeconds)
    } catch {
      setError('Nie udało się wygenerować tokenu. Spróbuj ponownie.')
      setSetupToken(null)
      setSecondsLeft(0)
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void fetchToken()
  }, [fetchToken])

  useEffect(() => {
    if (timerRef.current) {
      clearInterval(timerRef.current)
      timerRef.current = null
    }

    if (secondsLeft <= 0) return

    timerRef.current = setInterval(() => {
      setSecondsLeft((prev) => {
        if (prev <= 1) {
          if (timerRef.current) clearInterval(timerRef.current)
          timerRef.current = null
          return 0
        }
        return prev - 1
      })
    }, 1000)

    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
    }
  }, [secondsLeft])

  const qrValue = useMemo(
    () => buildQrPayload(serverUrl, setupToken ?? undefined),
    [serverUrl, setupToken],
  )

  const isExpired = secondsLeft <= 0 && setupToken !== null

  return (
    <section>
      <header className="page-header">
        <h1>Aplikacja mobilna</h1>
      </header>

      <div className="mobile-setup-container">
        <div className="mobile-setup-card">
          <h2 className="mobile-setup-heading">Połącz aplikację mobilną</h2>
          <p className="mobile-setup-description">
            Zeskanuj poniższy kod QR w aplikacji OpenKSeF Mobile, aby automatycznie skonfigurować
            adres serwera i zalogować się.
          </p>

          <div className="mobile-setup-qr-wrapper" data-testid="mobile-setup-qr">
            {isLoading ? (
              <div style={{ width: 280, height: 280, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                Generowanie...
              </div>
            ) : (
              <QRCodeSVG
                value={qrValue}
                size={280}
                level="M"
                marginSize={2}
                style={isExpired ? { opacity: 0.3, filter: 'grayscale(1)' } : undefined}
              />
            )}
          </div>

          {error ? (
            <div className="ui-form-error" role="alert" style={{ textAlign: 'center', marginTop: 12 }}>
              {error}
            </div>
          ) : null}

          <div className="mobile-setup-token-status" data-testid="mobile-setup-countdown" style={{ textAlign: 'center', margin: '12px 0' }}>
            {isExpired ? (
              <span style={{ color: 'var(--ui-color-error, #c0392b)' }}>
                Token wygasł -- wygeneruj nowy
              </span>
            ) : secondsLeft > 0 ? (
              <span>
                Token ważny jeszcze: <strong>{formatCountdown(secondsLeft)}</strong>
              </span>
            ) : null}
          </div>

          <div style={{ textAlign: 'center', marginBottom: 16 }}>
            <Button
              data-testid="mobile-setup-regenerate"
              onClick={() => void fetchToken()}
              disabled={isLoading}
            >
              {isLoading ? 'Generowanie...' : 'Wygeneruj nowy kod QR'}
            </Button>
          </div>

          <div className="mobile-setup-server-info">
            <span className="mobile-setup-server-label">Adres serwera:</span>
            <code className="mobile-setup-server-url">{serverUrl}</code>
          </div>

          <div className="mobile-setup-instructions">
            <h3>Instrukcja</h3>
            <ol>
              <li>Zainstaluj aplikację OpenKSeF Mobile na telefonie</li>
              <li>Przy pierwszym uruchomieniu pojawi się skaner kodów QR</li>
              <li>Zeskanuj powyższy kod -- adres serwera i login zostaną ustawione automatycznie</li>
            </ol>
          </div>
        </div>
      </div>
    </section>
  )
}
