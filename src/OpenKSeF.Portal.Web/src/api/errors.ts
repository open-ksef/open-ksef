const DEFAULT_MESSAGES: Record<number, string> = {
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  408: 'Request Timeout',
  409: 'Conflict',
  422: 'Unprocessable Entity',
  500: 'Server Error',
  502: 'Bad Gateway',
  503: 'Service Unavailable',
  504: 'Gateway Timeout',
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
  return DEFAULT_MESSAGES[status] ?? `Request failed with status ${status}`
}

async function extractResponseMessage(response: Response): Promise<string | null> {
  try {
    const contentType = response.headers.get('Content-Type') ?? ''
    const clonedResponse = response.clone()

    if (contentType.includes('application/json')) {
      const parsed = (await clonedResponse.json()) as Record<string, unknown>
      const message = parsed.message ?? parsed.error

      if (typeof message === 'string' && message.trim().length > 0) {
        return message
      }

      return null
    }

    const text = (await clonedResponse.text()).trim()
    return text.length > 0 ? text : null
  } catch {
    return null
  }
}
