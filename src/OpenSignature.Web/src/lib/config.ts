/**
 * API base URL from env. When unset or empty, uses same-origin paths (Vite `/api` proxy
 * → http://localhost:5270 by default). Set VITE_API_BASE_URL to call the API directly.
 */
export function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL
  if (raw === undefined || raw === '') {
    return ''
  }
  return raw.replace(/\/$/, '')
}

export function getTenantId(): string | undefined {
  const value = import.meta.env.VITE_TENANT_ID?.trim()
  return value ? value : undefined
}

export function getApiKey(): string | undefined {
  const value = import.meta.env.VITE_API_KEY?.trim()
  return value ? value : undefined
}

export function apiUrl(path: string): string {
  const base = getApiBaseUrl()
  const normalized = path.startsWith('/') ? path : `/${path}`
  return `${base}${normalized}`
}
