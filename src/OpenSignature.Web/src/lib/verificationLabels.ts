const LABELS: Record<string, string> = {
  SIG_VALID: 'Cryptographic signature verified.',
  SIG_CRYPTO_INVALID: 'Cryptographic signature check failed.',
  SIG_DOCUMENT_MODIFIED: 'Signed document appears to have been modified.',
  SIG_FORMAT_UNSUPPORTED: 'This signature format is not supported for verification.',
  SIG_PARSE_FAILED: 'The signed artifact could not be parsed.',
  SIG_CERTIFICATE_INVALID: 'The signer certificate did not pass validation.',
  SIG_NO_SIGNER: 'No signer certificate was found in the signature.',
  SIG_UNEXPECTED_SIGNER: 'Signer certificate does not match the expected certificate.',
  CERT_VALID: 'Certificate path is valid.',
  CERT_PARSE_FAILED: 'Certificate could not be parsed.',
  CERT_NOT_YET_VALID: 'Certificate is not yet valid.',
  CERT_EXPIRED: 'Certificate has expired.',
  CERT_UNTRUSTED: 'Certificate is not trusted.',
  CERT_CHAIN_INVALID: 'Certificate chain is invalid.',
  CERT_REVOKED: 'Certificate is revoked.',
  CERT_REVOCATION_UNKNOWN: 'Revocation status could not be determined.',
  CERT_KEY_USAGE_INVALID: 'Certificate key usage is not valid for signing.',
  CERT_EKU_INVALID: 'Certificate extended key usage is not valid for signing.',
  CERT_POLICY_INVALID: 'Certificate policy constraint was not met.',
}

export function reasonCodeLabel(code: string): string {
  return LABELS[code] ?? 'See reason code.'
}
