type AccessTokenProvider = () => string | null | undefined

let currentAccessTokenProvider: AccessTokenProvider = () => null

export function setAccessTokenProvider(provider: AccessTokenProvider): void {
  currentAccessTokenProvider = provider
}

export function clearAccessTokenProvider(): void {
  currentAccessTokenProvider = () => null
}

export function getAccessToken(): string | null {
  return currentAccessTokenProvider() ?? null
}

interface SessionHandlers {
  renew: () => Promise<string | null>
  end: () => Promise<void>
}

const noSession: SessionHandlers = { renew: async () => null, end: async () => {} }
let currentSessionHandlers = noSession

export function setSessionHandlers(handlers: SessionHandlers): void {
  currentSessionHandlers = handlers
}

export function clearSessionHandlers(): void {
  currentSessionHandlers = noSession
}

export function renewSession(): Promise<string | null> {
  return currentSessionHandlers.renew()
}

export function endSession(): Promise<void> {
  return currentSessionHandlers.end()
}
