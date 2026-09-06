import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { listCertificates, listProviders } from '../api/providers'
import { createSignature, getSignature } from '../api/signatures'
import { ApiError } from '../api/types'
import type {
  CertificateListItem,
  ProviderListItem,
  SignatureFormat,
  SignatureProfile,
  SigningProviderType,
} from '../api/types'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime, isTerminalStatus, shortId } from '../lib/format'
import {
  clearTrackedSignatureIds,
  readTrackedSignatureIds,
  trackSignatureId,
} from '../lib/sessionSignatures'
import { certificateOptionKey, parseSigningProviderType } from '../lib/signingProviders'

const FORMATS: SignatureFormat[] = ['PAdES', 'XAdES', 'CAdES', 'ASiC_S', 'ASiC_E']
const PROFILES: SignatureProfile[] = ['B', 'T', 'LT', 'LTA']
const CERT_DEFAULT = 'default'

interface SignatureDashboardPageProps {
  title: string
  intro: string
}

export function SignatureDashboardPage({ title, intro }: SignatureDashboardPageProps) {
  const queryClient = useQueryClient()
  const [trackedIds, setTrackedIds] = useState(() => readTrackedSignatureIds())
  const [file, setFile] = useState<File | null>(null)
  const [format, setFormat] = useState<SignatureFormat>('PAdES')
  const [profile, setProfile] = useState<SignatureProfile>('B')
  const [selectedProviderId, setSelectedProviderId] = useState('')
  const [selectedCertKey, setSelectedCertKey] = useState<string | null>(null)
  const [visibleSignature, setVisibleSignature] = useState(false)
  const [signatureNote, setSignatureNote] = useState('')
  const [signaturePage, setSignaturePage] = useState('1')
  const [signatureImage, setSignatureImage] = useState<File | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const providersQuery = useQuery({
    queryKey: ['providers'],
    queryFn: listProviders,
    staleTime: 30_000,
  })
  const certificatesQuery = useQuery({
    queryKey: ['certificates'],
    queryFn: listCertificates,
    staleTime: 15_000,
  })

  const providers = providersQuery.data ?? []
  const certificates = certificatesQuery.data ?? []
  const selectedProvider = providers.find((provider) => provider.id === selectedProviderId) ?? null
  const providerCertificates = useMemo(
    () => certificates.filter((certificate) => certificate.providerId === selectedProviderId),
    [certificates, selectedProviderId],
  )
  const selectedCertificate = providerCertificates.find(
    (certificate) => certificateOptionKey(certificate.providerId, certificate.thumbprint) === selectedCertKey,
  )

  useEffect(() => {
    if (selectedProviderId || providers.length === 0) {
      return
    }

    setSelectedProviderId(providers[0].id)
    setSelectedCertKey(null)
  }, [providers, selectedProviderId])

  useEffect(() => {
    if (selectedCertKey !== null || !selectedProviderId || !certificatesQuery.isSuccess) {
      return
    }

    const firstSignable = providerCertificates.find((certificate) => certificate.canSign)
    setSelectedCertKey(
      firstSignable
        ? certificateOptionKey(firstSignable.providerId, firstSignable.thumbprint)
        : CERT_DEFAULT,
    )
  }, [certificatesQuery.isSuccess, providerCertificates, selectedCertKey, selectedProviderId])

  const statusQueries = useQueries({
    queries: trackedIds.map((id) => ({
      queryKey: ['signature', id],
      queryFn: () => getSignature(id),
      refetchInterval: (query: { state: { data?: { status?: string } } }) =>
        isTerminalStatus(query.state.data?.status) ? false : 2000,
      retry: 1,
    })),
  })

  const createMutation = useMutation({
    mutationFn: createSignature,
    onSuccess: (created) => {
      const next = trackSignatureId(created.id)
      setTrackedIds(next)
      void queryClient.invalidateQueries({ queryKey: ['signature', created.id] })
      setFormError(null)
      setFile(null)
      setSignatureImage(null)
      setSignatureNote('')
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError) {
        setFormError(
          error.errorCode ? `${error.errorCode}: ${error.message}` : error.message,
        )
        return
      }
      setFormError(error instanceof Error ? error.message : 'Create failed')
    },
  })

  function onProviderChange(providerId: string) {
    setSelectedProviderId(providerId)
    setSelectedCertKey(null)
    setFormError(null)
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!file) {
      setFormError('Choose a file to sign.')
      return
    }

    const signingProvider = resolveProviderType(selectedProvider)
    if (!signingProvider) {
      setFormError('Choose a registered signing provider.')
      return
    }

    if (selectedCertificate && !selectedCertificate.canSign) {
      setFormError(
        'The selected certificate cannot sign. Choose a certificate with Can sign = Yes, or check the token PIN and validity.',
      )
      return
    }

    if (format === 'PAdES' && visibleSignature) {
      const page = Number.parseInt(signaturePage, 10)
      if (!Number.isInteger(page) || page < 1) {
        setFormError('Signature page must be a positive number.')
        return
      }
    }

    createMutation.mutate({
      file,
      format,
      profile,
      signingProvider,
      certificateThumbprint:
        selectedCertificate && selectedCertKey !== CERT_DEFAULT
          ? selectedCertificate.thumbprint
          : undefined,
      visibleSignature: format === 'PAdES' && visibleSignature,
      signatureNote: format === 'PAdES' && visibleSignature ? signatureNote || undefined : undefined,
      signaturePage:
        format === 'PAdES' && visibleSignature
          ? Number.parseInt(signaturePage, 10) || 1
          : undefined,
      signatureImage: format === 'PAdES' && visibleSignature ? signatureImage : undefined,
    })
  }

  function onClearTracked() {
    clearTrackedSignatureIds()
    setTrackedIds([])
  }

  const providersError =
    providersQuery.error instanceof ApiError
      ? `${providersQuery.error.errorCode ?? 'ERROR'}: ${providersQuery.error.message}`
      : providersQuery.error instanceof Error
        ? providersQuery.error.message
        : null
  const certificatesError =
    certificatesQuery.error instanceof ApiError
      ? `${certificatesQuery.error.errorCode ?? 'ERROR'}: ${certificatesQuery.error.message}`
      : certificatesQuery.error instanceof Error
        ? certificatesQuery.error.message
        : null

  const isHardwareProvider =
    selectedProvider?.providerType === 'SmartCard' || selectedProvider?.providerType === 'Pkcs11'

  return (
    <section className="page">
      <header className="page-header">
        <h1>{title}</h1>
        <p>{intro}</p>
      </header>

      <div className="split">
        <form className="stack-form" onSubmit={onSubmit}>
          <h2>Create signature</h2>
          <label className="field">
            <span>Document</span>
            <input
              type="file"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <div className="field-row">
            <label className="field">
              <span>Format</span>
              <select value={format} onChange={(e) => setFormat(e.target.value as SignatureFormat)}>
                {FORMATS.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Profile</span>
              <select
                value={profile}
                onChange={(e) => setProfile(e.target.value as SignatureProfile)}
              >
                {PROFILES.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>Provider</span>
              <select
                value={selectedProviderId}
                onChange={(e) => onProviderChange(e.target.value)}
                disabled={providersQuery.isLoading || providers.length === 0}
              >
                {providers.length === 0 ? (
                  <option value="">No providers registered</option>
                ) : (
                  providers.map((provider) => (
                    <option key={provider.id} value={provider.id}>
                      {provider.name} ({provider.providerType})
                    </option>
                  ))
                )}
              </select>
            </label>
          </div>
          {providersError ? <p className="error-text">{providersError}</p> : null}
          {profile !== 'B' ? (
            <p className="empty">
              T, LT, and LTA require a configured RFC 3161 timestamp authority. LT and LTA also
              need CRL or OCSP evidence. OpenSignature never silently downgrades to Baseline B.
            </p>
          ) : null}
          <label className="field">
            <span>Certificate</span>
            <select
              value={selectedCertKey ?? ''}
              onChange={(e) => setSelectedCertKey(e.target.value || CERT_DEFAULT)}
              disabled={!selectedProviderId || certificatesQuery.isLoading}
            >
              <option value={CERT_DEFAULT}>Use provider default (first signable certificate)</option>
              {providerCertificates.map((certificate) => (
                <option
                  key={certificateOptionKey(certificate.providerId, certificate.thumbprint)}
                  value={certificateOptionKey(certificate.providerId, certificate.thumbprint)}
                  disabled={!certificate.canSign}
                >
                  {certificateLabel(certificate)}
                </option>
              ))}
            </select>
          </label>
          {certificatesQuery.isLoading ? <p className="muted">Loading certificates…</p> : null}
          {certificatesError ? <p className="error-text">{certificatesError}</p> : null}
          {certificatesQuery.isSuccess && selectedProviderId && providerCertificates.length === 0 ? (
            <p className="empty">
              No certificates were returned for this provider.{' '}
              {isHardwareProvider ? (
                <>
                  Confirm the USB token is plugged in, PKCS#11 middleware is installed, and the PIN
                  is set in user secrets (listing logs in). See{' '}
                  <Link to="/providers">Providers</Link>.
                </>
              ) : (
                <>
                  Check the <Link to="/certificates">Certificates</Link> page.
                </>
              )}
            </p>
          ) : null}
          {selectedCertificate ? (
            <dl className="cert-details">
              <div>
                <dt>Subject</dt>
                <dd>{selectedCertificate.subject}</dd>
              </div>
              <div>
                <dt>Thumbprint</dt>
                <dd className="mono small wrap">{selectedCertificate.thumbprint}</dd>
              </div>
              <div>
                <dt>Valid until</dt>
                <dd>
                  {formatDateTime(selectedCertificate.notAfter)}
                  {selectedCertificate.isCurrentlyValid ? '' : ' (expired or not yet valid)'}
                </dd>
              </div>
              <div>
                <dt>Can sign</dt>
                <dd>{selectedCertificate.canSign ? 'Yes' : 'No'}</dd>
              </div>
            </dl>
          ) : (
            <p className="note">
              Choose a certificate from the selected provider. USB tokens appear under SmartCard.
              The worker uses this thumbprint, so a PFX certificate cannot be used with a USB
              provider.
            </p>
          )}
          {format === 'PAdES' ? (
            <fieldset className="appearance-fields">
              <legend>Visible PDF appearance</legend>
              <label className="field checkbox">
                <input
                  type="checkbox"
                  checked={visibleSignature}
                  onChange={(event) => setVisibleSignature(event.target.checked)}
                />
                <span>Show a visible signature stamp on the PDF</span>
              </label>
              {visibleSignature ? (
                <>
                  <p className="note">
                    The stamp includes “Digitally signed by …” from the certificate, the signing
                    time, and an optional note or image.
                  </p>
                  <label className="field">
                    <span>Note (optional)</span>
                    <textarea
                      rows={3}
                      maxLength={500}
                      value={signatureNote}
                      onChange={(event) => setSignatureNote(event.target.value)}
                      placeholder="Approved / Signed by …"
                    />
                  </label>
                  <div className="field-row appearance-row">
                    <label className="field">
                      <span>Page</span>
                      <input
                        type="number"
                        min={1}
                        value={signaturePage}
                        onChange={(event) => setSignaturePage(event.target.value)}
                      />
                    </label>
                    <label className="field">
                      <span>Image (optional JPEG or PNG)</span>
                      <input
                        type="file"
                        accept="image/jpeg,image/png,.jpg,.jpeg,.png"
                        onChange={(event) =>
                          setSignatureImage(event.target.files?.[0] ?? null)
                        }
                      />
                    </label>
                  </div>
                </>
              ) : null}
            </fieldset>
          ) : null}
          {formError ? <p className="error-text">{formError}</p> : null}
          <button type="submit" className="btn primary" disabled={createMutation.isPending}>
            {createMutation.isPending ? 'Submitting…' : 'Submit for signing'}
          </button>
        </form>

        <div className="list-panel">
          <div className="list-panel-head">
            <h2>Session signatures</h2>
            {trackedIds.length > 0 ? (
              <button type="button" className="btn ghost" onClick={onClearTracked}>
                Clear list
              </button>
            ) : null}
          </div>
          <p className="note">
            The API has no list-all endpoint yet. This view tracks IDs created in this browser
            session and polls each with <code>GET /api/v1/signatures/{'{id}'}</code>.
          </p>
          {trackedIds.length === 0 ? (
            <p className="empty">No signatures tracked in this session.</p>
          ) : (
            <ul className="signature-list">
              {trackedIds.map((id, index) => {
                const query = statusQueries[index]
                const data = query?.data
                const error =
                  query?.error instanceof ApiError
                    ? query.error.errorCode ?? query.error.message
                    : query?.error instanceof Error
                      ? query.error.message
                      : null

                return (
                  <li key={id}>
                    <Link to={`/signatures/${id}`} className="signature-row">
                      <span className="mono">{shortId(id)}</span>
                      {data ? <StatusBadge status={data.status} /> : null}
                      {query?.isLoading ? <span className="muted">Loading…</span> : null}
                      {error ? <span className="error-text">{error}</span> : null}
                      {data ? (
                        <span className="muted">{formatDateTime(data.createdAt)}</span>
                      ) : null}
                    </Link>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </div>
    </section>
  )
}

function resolveProviderType(provider: ProviderListItem | null): SigningProviderType | null {
  return parseSigningProviderType(provider?.providerType)
}

function certificateLabel(certificate: CertificateListItem): string {
  const name = certificate.friendlyName?.trim() || certificate.subject
  const signable = certificate.canSign ? 'can sign' : 'cannot sign'
  const validity = certificate.isCurrentlyValid ? 'valid' : 'not valid'
  return `${name} — ${signable}, ${validity}`
}
