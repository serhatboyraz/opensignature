import { apiJson } from './client'
import type { SignatureFormat } from './types'

export type VerificationOverallStatus = 'VALID' | 'INVALID' | 'INDETERMINATE' | string

export interface RevocationReport {
  status: string
  source: string
  detail?: string | null
}

export interface CertificatePathReport {
  isValid: boolean
  subject?: string | null
  issuer?: string | null
  thumbprint?: string | null
  notBefore?: string | null
  notAfter?: string | null
  reasonCodes: string[]
  chainStatus: string[]
  revocation?: RevocationReport | null
}

export interface SignatureCryptoCheck {
  cryptoValid: boolean
  signerThumbprint?: string | null
  signerSubject?: string | null
  reasonCodes: string[]
}

export interface SignatureVerificationReport {
  overallStatus: VerificationOverallStatus
  isValid: boolean
  reasonCodes: string[]
  checkedAt: string
  source: string
  format?: string | null
  signatureId?: string | null
  detail?: string | null
  limitations: string
  signature?: SignatureCryptoCheck | null
  certificate?: CertificatePathReport | null
}

export function verifyStoredSignature(id: string): Promise<SignatureVerificationReport> {
  return apiJson<SignatureVerificationReport>(`/api/v1/signatures/${id}/verification`)
}

export interface VerifyUploadedInput {
  file: File
  format: SignatureFormat
  originalFile?: File | null
}

export function verifyUploadedSignature(
  input: VerifyUploadedInput,
): Promise<SignatureVerificationReport> {
  const form = new FormData()
  form.append('file', input.file)
  form.append('format', input.format)
  if (input.originalFile) {
    form.append('originalFile', input.originalFile)
  }

  return apiJson<SignatureVerificationReport>('/api/v1/verifications', {
    method: 'POST',
    body: form,
    skipJsonContentType: true,
  })
}
