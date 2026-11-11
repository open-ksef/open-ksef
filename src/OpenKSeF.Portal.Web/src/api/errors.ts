import type { ValidationEnvelope } from './types'

const DEFAULT_MESSAGES: Record<number, string> = {
  400: 'Serwer nie przyjął żądania.',
  401: 'Sesja wygasła. Zaloguj się ponownie.',
  403: 'Brak uprawnień do tej operacji.',
  404: 'Nie znaleziono.',
  408: 'Serwer nie odpowiedział na czas.',
  409: 'Operacja jest teraz niemożliwa. Odśwież stronę.',
  413: 'Plik jest za duży.',
  422: 'Serwer nie przyjął danych.',
  429: 'Za dużo żądań. Spróbuj ponownie za chwilę.',
  500: 'Błąd serwera. Spróbuj ponownie później.',
  502: 'Serwer jest chwilowo niedostępny.',
  503: 'Serwer jest chwilowo niedostępny.',
  504: 'Serwer nie odpowiedział na czas.',
}

export class ApiError extends Error {
  readonly status: number
  readonly originalError?: unknown

  constructor(message: string, status = 500, originalError?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.originalError = originalError
  }
}

export async function normalizeError(input: unknown): Promise<ApiError> {
  if (input instanceof ApiError) {
    return input
  }

  if (input instanceof Response) {
    const message = await extractResponseMessage(input)
    return new ApiError(message ?? getDefaultMessage(input.status), input.status, input)
  }

  if (isAbortError(input)) {
    return new ApiError(getDefaultMessage(408), 408, input)
  }

  if (isNetworkError(input)) {
    return new ApiError(getDefaultMessage(503), 503, input)
  }

  if (input instanceof Error) {
    return new ApiError(input.message || getDefaultMessage(500), 500, input)
  }

  return new ApiError(getDefaultMessage(500), 500, input)
}

export async function readValidationEnvelope(error: unknown): Promise<ValidationEnvelope | null> {
  if (!(error instanceof ApiError) || !(error.originalError instanceof Response)) {
    return null
  }

  try {
    const parsed = (await error.originalError.clone().json()) as Partial<ValidationEnvelope>
    return Array.isArray(parsed.errors)
      ? { errors: parsed.errors, warnings: Array.isArray(parsed.warnings) ? parsed.warnings : [] }
      : null
  } catch {
    return null
  }
}

export async function readFieldError(error: unknown, field: string): Promise<string | null> {
  if (!(error instanceof ApiError) || error.status !== 400 || !(error.originalError instanceof Response)) {
    return null
  }

  try {
    const parsed = (await error.originalError.clone().json()) as { errors?: Record<string, unknown> }
    const messages = parsed.errors?.[field]
    return Array.isArray(messages) && typeof messages[0] === 'string' ? messages[0] : null
  } catch {
    return null
  }
}

function isAbortError(input: unknown): boolean {
  if (!input || typeof input !== 'object') {
    return false
  }

  const error = input as { name?: string }
  return error.name === 'AbortError'
}

function isNetworkError(input: unknown): boolean {
  if (!(input instanceof TypeError)) {
    return false
  }

  const normalizedMessage = input.message.toLowerCase()
  return normalizedMessage.includes('fetch') || normalizedMessage.includes('network')
}

function getDefaultMessage(status: number): string {
  return DEFAULT_MESSAGES[status] ?? `Żądanie nie powiodło się (kod ${status}).`
}

async function extractResponseMessage(response: Response): Promise<string | null> {
  try {
    const contentType = response.headers.get('Content-Type') ?? ''
    const clonedResponse = response.clone()

    if (contentType.includes('json')) {
      const parsed = (await clonedResponse.json()) as Record<string, unknown>
      const message = parsed.message ?? parsed.error

      if (typeof message === 'string' && message.trim().length > 0) {
        return message
      }

      return null
    }

    if (response.status === 413) {
      return null
    }

    const text = (await clonedResponse.text()).trim()
    return text.length > 0 ? text : null
  } catch {
    return null
  }
}
