import { apiJson } from './client'
import type { CertificateListItem, ProviderHealth, ProviderListItem } from './types'

export function listCertificates(): Promise<CertificateListItem[]> {
  return apiJson<CertificateListItem[]>('/api/v1/certificates')
}

export function listProviders(): Promise<ProviderListItem[]> {
  return apiJson<ProviderListItem[]>('/api/v1/providers')
}

export function getProviderHealth(id: string): Promise<ProviderHealth> {
  return apiJson<ProviderHealth>(`/api/v1/providers/${encodeURIComponent(id)}/health`)
}
