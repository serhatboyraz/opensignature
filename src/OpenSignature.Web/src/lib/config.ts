/**
 * API base URL from env. When unset, defaults to the Api https launch profile
 * (https://localhost:7010). An empty string uses same-origin paths (Vite proxy).
 */
export function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL
  if (raw === undefined) {
    return 'https://localhost:7010'
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
