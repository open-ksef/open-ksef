import { getAccessToken } from '@/auth/accessToken'
import { normalizeError } from './errors'

export type QueryValue = string | number | boolean | null | undefined

export interface RequestOptions {
  headers?: HeadersInit
  query?: Record<string, QueryValue>
  body?: unknown
  signal?: AbortSignal
}

interface ClientRequestInit extends RequestOptions {
  method?: string
}

export interface ApiClientOptions {
  getToken?: () => string | null | undefined
}

export class ApiClient {
  private readonly baseUrl: string
  private readonly getToken?: () => string | null | undefined

  constructor(baseUrl: string, options?: ApiClientOptions) {
    this.baseUrl = baseUrl.replace(/\/+$/, '')
    this.getToken = options?.getToken
  }

  async request<TResponse>(path: string, init?: ClientRequestInit): Promise<TResponse> {
    const method = init?.method?.toUpperCase() ?? 'GET'
    const url = this.buildUrl(path, init?.query)
    const headers = new Headers(init?.headers)

    if (!headers.has('Authorization')) {
      const token = this.getToken?.()
      if (token) {
        headers.set('Authorization', `Bearer ${token}`)
      }
    }

    const body = this.serializeBody(init?.body, headers)

    try {
      const response = await fetch(url, {
        method,
        headers,
        body,
        signal: init?.signal,
      })

      if (!response.ok) {
        throw await normalizeError(response)
      }

      if (response.status === 204) {
        return undefined as TResponse
      }

      const contentType = response.headers.get('Content-Type') ?? ''
      if (contentType.includes('application/json')) {
        return (await response.json()) as TResponse
      }

      return (await response.text()) as TResponse
    } catch (error) {
      throw await normalizeError(error)
    }
  }

  get<TResponse>(path: string, options?: Omit<RequestOptions, 'body'>): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: 'GET' })
  }

  post<TResponse>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body'>): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: 'POST', body })
  }

  put<TResponse>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body'>): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: 'PUT', body })
  }

  patch<TResponse>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body'>): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: 'PATCH', body })
  }

  delete<TResponse>(path: string, options?: Omit<RequestOptions, 'body'>): Promise<TResponse> {
    return this.request<TResponse>(path, { ...options, method: 'DELETE' })
  }

  private buildUrl(path: string, query?: Record<string, QueryValue>): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`
    const url = new URL(this.toAbsoluteUrl(`${this.baseUrl}${normalizedPath}`))

    if (query) {
      for (const [key, value] of Object.entries(query)) {
        if (value === undefined || value === null) {
          continue
        }

        url.searchParams.set(key, String(value))
      }
    }

    return url.toString()
  }

  private toAbsoluteUrl(url: string): string {
    if (url.startsWith('http://') || url.startsWith('https://')) {
      return url
    }

    const origin = globalThis.location?.origin ?? 'http://localhost'
    const normalizedRelativeUrl = url.startsWith('/') ? url : `/${url}`
    return `${origin}${normalizedRelativeUrl}`
  }

  private serializeBody(body: unknown, headers: Headers): BodyInit | undefined {
    if (body === undefined || body === null) {
      return undefined
    }

    if (
      typeof body === 'string' ||
      body instanceof FormData ||
      body instanceof URLSearchParams ||
      body instanceof Blob ||
      body instanceof ArrayBuffer
    ) {
      return body
    }

    if (!headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json')
    }

    return JSON.stringify(body)
  }
}

function resolveApiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL ?? '/api'
}

export const apiClient = new ApiClient(resolveApiBaseUrl(), {
  getToken: () => getAccessToken(),
})
