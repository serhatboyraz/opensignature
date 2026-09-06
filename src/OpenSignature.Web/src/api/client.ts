import { apiUrl, getApiKey, getTenantId } from '../lib/config'
import { ApiError, type ProblemDetails } from './types'

export type ApiRequestInit = RequestInit & {
  /** When true, skip JSON Content-Type (e.g. FormData uploads). */
  skipJsonContentType?: boolean
}

function buildHeaders(init?: ApiRequestInit): Headers {
  const headers = new Headers(init?.headers)

  const tenantId = getTenantId()
  if (tenantId && !headers.has('X-Tenant-Id')) {
    headers.set('X-Tenant-Id', tenantId)
  }

  const apiKey = getApiKey()
  if (apiKey && !headers.has('X-Api-Key')) {
    headers.set('X-Api-Key', apiKey)
  }

  if (
    !init?.skipJsonContentType &&
    init?.body &&
    !(init.body instanceof FormData) &&
    !headers.has('Content-Type')
  ) {
    headers.set('Content-Type', 'application/json')
  }

  return headers
}

async function parseProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) {
    return undefined
  }

  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

export async function apiFetch(path: string, init?: ApiRequestInit): Promise<Response> {
  const response = await fetch(apiUrl(path), {
    ...init,
    headers: buildHeaders(init),
  })

  if (!response.ok) {
    const problem = await parseProblem(response)
    throw new ApiError(
      problem?.detail ?? problem?.title ?? `Request failed (${response.status})`,
      response.status,
      problem,
    )
  }

  return response
}

export async function apiJson<T>(path: string, init?: ApiRequestInit): Promise<T> {
  const response = await apiFetch(path, init)
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}
