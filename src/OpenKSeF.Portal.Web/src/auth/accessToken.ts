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
