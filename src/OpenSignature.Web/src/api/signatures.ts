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
  visibleSignature?: boolean
  signatureNote?: string
  signaturePage?: number
  signatureImage?: File | null
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
  if (input.format === 'PAdES' && input.visibleSignature) {
    form.append('visibleSignature', 'true')
    if (input.signatureNote?.trim()) {
      form.append('signatureNote', input.signatureNote.trim())
    }
    if (input.signaturePage && input.signaturePage > 0) {
      form.append('signaturePage', String(input.signaturePage))
    }
    if (input.signatureImage) {
      form.append('signatureImage', input.signatureImage)
    }
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
  const contentType = response.headers.get('Content-Type')
  const fromHeader = parseFileName(disposition)
  const blob = await response.blob()
  if (blob.size === 0) {
    throw new Error('Download returned an empty file (0 bytes).')
  }

  const fileName =
    preferExtension(fromHeader, contentType) ??
    `signature-${id}${extensionForContentType(contentType)}`
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

function extensionForContentType(contentType: string | null): string {
  const mime = (contentType ?? '').split(';')[0]?.trim().toLowerCase()
  switch (mime) {
    case 'application/pdf':
      return '.pdf'
    case 'application/xml':
    case 'text/xml':
      return '.xml'
    case 'application/pkcs7-mime':
    case 'application/pkcs7-signature':
      return '.p7m'
    default:
      return '.bin'
  }
}

/** When the API still uses a generic signed.bin name, align the extension with Content-Type. */
function preferExtension(
  fileName: string | undefined,
  contentType: string | null,
): string | undefined {
  if (!fileName) {
    return undefined
  }

  const preferred = extensionForContentType(contentType)
  if (preferred === '.bin') {
    return fileName
  }

  if (/\.bin$/i.test(fileName) || !/\.[a-z0-9]+$/i.test(fileName)) {
    return fileName.replace(/(\.bin)?$/i, preferred)
  }

  return fileName
}
