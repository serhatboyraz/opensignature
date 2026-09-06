export type SignatureStatus =
  | 'Queued'
  | 'Processing'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | string

export type SignatureFormat = 'PAdES' | 'XAdES' | 'CAdES' | 'ASiC_S' | 'ASiC_E'
export type SignatureProfile = 'B' | 'T' | 'LT' | 'LTA'
export type SigningProviderType = 'Pfx' | 'Pkcs11' | 'SmartCard' | 'Hsm'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errorCode?: string
  traceId?: string
}

export interface SignatureCreateResponse {
  id: string
  status: SignatureStatus
  createdAt: string
  statusUrl: string
}

export interface SignatureStatusResponse {
  id: string
  tenantId: string
  status: SignatureStatus
  format: string
  profile: string
  signingProvider: string
  createdAt: string
  queuedAt?: string | null
  startedAt?: string | null
  completedAt?: string | null
  failedAt?: string | null
  errorCode?: string | null
  errorMessage?: string | null
  correlationId?: string | null
  visibleSignature?: boolean
  signatureNote?: string | null
  appearancePageNumber?: number
  hasAppearanceImage?: boolean
  statusUrl: string
}

export interface CertificateListItem {
  id: string
  thumbprint: string
  subject: string
  issuer: string
  serialNumber: string
  notBefore: string
  notAfter: string
  providerId: string
  providerReference: string
  canSign: boolean
  friendlyName?: string | null
  publicKeyAlgorithm?: string | null
  keySizeBits?: number | null
  keyUsages?: string[]
  enhancedKeyUsages?: string[]
  isCurrentlyValid: boolean
}

export interface ProviderListItem {
  id: string
  name: string
  providerType: string
}

export interface ProviderHealth {
  id: string
  state: string
  isHealthy: boolean
  detail?: string | null
  checkedAt: string
}

export class ApiError extends Error {
  readonly status: number
  readonly errorCode?: string
  readonly problem?: ProblemDetails

  constructor(message: string, status: number, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.errorCode = problem?.errorCode
  }
}
