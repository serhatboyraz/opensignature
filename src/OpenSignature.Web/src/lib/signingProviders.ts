import type { SigningProviderType } from '../api/types'

const PROVIDER_TYPES: readonly SigningProviderType[] = ['Pfx', 'Pkcs11', 'SmartCard', 'Hsm']

export function parseSigningProviderType(value: string | undefined | null): SigningProviderType | null {
  if (!value) {
    return null
  }

  return PROVIDER_TYPES.includes(value as SigningProviderType) ? (value as SigningProviderType) : null
}

export function certificateOptionKey(providerId: string, thumbprint: string): string {
  return `${providerId}::${thumbprint}`
}
