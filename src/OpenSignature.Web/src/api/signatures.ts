import { apiFetch, apiJson } from './client'
import type {
  SignatureCreateResponse,
  SignatureFormat,
  SignatureProfile,
  SignatureStatusResponse,
  SigningProviderType,
} from './types'

export interface CreateSignatureInput {
  file: File
  format: SignatureFormat
  profile: SignatureProfile
  signingProvider: SigningProviderType
  certificateThumbprint?: string
}

export async function createSignature(
  input: CreateSignatureInput,
): Promise<SignatureCreateResponse> {
  const form = new FormData()
  form.append('file', input.file)
  form.append('format', input.format)
  form.append('profile', input.profile)
  form.append('signingProvider', input.signingProvider)
  if (input.certificateThumbprint?.trim()) {
    form.append('certificateThumbprint', input.certificateThumbprint.trim())
  }

  return apiJson<SignatureCreateResponse>('/api/v1/signatures', {
    method: 'POST',
    body: form,
    skipJsonContentType: true,
    headers: {
      'Idempotency-Key': crypto.randomUUID(),
    },
  })
}

export function getSignature(id: string): Promise<SignatureStatusResponse> {
  return apiJson<SignatureStatusResponse>(`/api/v1/signatures/${id}`)
}

export function cancelSignature(id: string): Promise<SignatureStatusResponse> {
  return apiJson<SignatureStatusResponse>(`/api/v1/signatures/${id}/cancel`, {
    method: 'POST',
  })
}

export async function downloadSignatureContent(id: string): Promise<{
  blob: Blob
  fileName: string
}> {
  const response = await apiFetch(`/api/v1/signatures/${id}/content`)
  const disposition = response.headers.get('Content-Disposition')
  const fileName = parseFileName(disposition) ?? `signature-${id}.bin`
  const blob = await response.blob()
  return { blob, fileName }
}

function parseFileName(disposition: string | null): string | undefined {
  if (!disposition) {
    return undefined
  }

  const utfMatch = /filename\*=UTF-8''([^;]+)/i.exec(disposition)
  if (utfMatch?.[1]) {
    return decodeURIComponent(utfMatch[1])
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(disposition)
  return plainMatch?.[1]
}
