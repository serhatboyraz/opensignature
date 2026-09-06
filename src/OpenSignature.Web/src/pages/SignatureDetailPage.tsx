import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../api/types'
import {
  cancelSignature,
  downloadSignatureContent,
  getSignature,
} from '../api/signatures'
import { verifyStoredSignature } from '../api/verification'
import { StatusBadge } from '../components/StatusBadge'
import { VerificationReportPanel } from '../components/VerificationReportPanel'
import { formatDateTime, isTerminalStatus } from '../lib/format'
import { apiUrl } from '../lib/config'

export function SignatureDetailPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()

  const statusQuery = useQuery({
    queryKey: ['signature', id],
    queryFn: () => getSignature(id),
    enabled: Boolean(id),
    refetchInterval: (query) =>
      isTerminalStatus(query.state.data?.status) ? false : 2000,
  })

  const cancelMutation = useMutation({
    mutationFn: () => cancelSignature(id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['signature', id], updated)
    },
  })

  const downloadMutation = useMutation({
    mutationFn: () => downloadSignatureContent(id),
    onSuccess: ({ blob, fileName }) => {
      // Do not revoke immediately after click — the browser reads the blob
      // asynchronously; early revoke yields empty (0 KB) downloads.
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = fileName
      anchor.rel = 'noopener'
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    },
  })

  const verificationQuery = useQuery({
    queryKey: ['signature-verification', id],
    queryFn: () => verifyStoredSignature(id),
    enabled: Boolean(id) && statusQuery.data?.status === 'Completed',
    retry: 1,
  })

  if (!id) {
    return (
      <section className="page">
        <p className="error-text">Missing signature id.</p>
      </section>
    )
  }

  const data = statusQuery.data
  const loadError =
    statusQuery.error instanceof ApiError
      ? `${statusQuery.error.errorCode ?? 'ERROR'}: ${statusQuery.error.message}`
      : statusQuery.error instanceof Error
        ? statusQuery.error.message
        : null

  const cancelError =
    cancelMutation.error instanceof ApiError
      ? `${cancelMutation.error.errorCode ?? 'ERROR'}: ${cancelMutation.error.message}`
      : cancelMutation.error instanceof Error
        ? cancelMutation.error.message
        : null

  const canCancel =
    data &&
    data.status !== 'Completed' &&
    data.status !== 'Failed' &&
    data.status !== 'Cancelled'

  return (
    <section className="page">
      <header className="page-header">
        <p className="eyebrow-link">
          <Link to="/dashboard">← Dashboard</Link>
        </p>
        <h1>Signature detail</h1>
        <p className="mono wrap">{id}</p>
      </header>

      {statusQuery.isLoading ? <p className="muted">Loading status…</p> : null}
      {loadError ? <p className="error-text">{loadError}</p> : null}

      {data ? (
        <>
          <dl className="detail-grid">
            <div>
              <dt>Status</dt>
              <dd>
                <StatusBadge status={data.status} />
              </dd>
            </div>
            <div>
              <dt>Format</dt>
              <dd>
                {data.format} / {data.profile}
              </dd>
            </div>
            <div>
              <dt>Provider</dt>
              <dd>{data.signingProvider}</dd>
            </div>
            <div>
              <dt>Tenant</dt>
              <dd>{data.tenantId}</dd>
            </div>
            <div>
              <dt>Created</dt>
              <dd>{formatDateTime(data.createdAt)}</dd>
            </div>
            <div>
              <dt>Queued</dt>
              <dd>{formatDateTime(data.queuedAt)}</dd>
            </div>
            <div>
              <dt>Started</dt>
              <dd>{formatDateTime(data.startedAt)}</dd>
            </div>
            <div>
              <dt>Completed</dt>
              <dd>{formatDateTime(data.completedAt)}</dd>
            </div>
            <div>
              <dt>Failed</dt>
              <dd>{formatDateTime(data.failedAt)}</dd>
            </div>
            <div>
              <dt>Correlation</dt>
              <dd>{data.correlationId ?? '—'}</dd>
            </div>
            <div>
              <dt>Visible signature</dt>
              <dd>{data.visibleSignature ? 'Yes' : 'No'}</dd>
            </div>
            <div>
              <dt>Appearance page</dt>
              <dd>{data.visibleSignature ? data.appearancePageNumber ?? 1 : '—'}</dd>
            </div>
            <div className="span-2">
              <dt>Signature note</dt>
              <dd>{data.signatureNote?.trim() ? data.signatureNote : '—'}</dd>
            </div>
            <div>
              <dt>Appearance image</dt>
              <dd>{data.hasAppearanceImage ? 'Yes' : 'No'}</dd>
            </div>
            <div className="span-2">
              <dt>Error code</dt>
              <dd className={data.errorCode ? 'error-text' : undefined}>
                {data.errorCode ?? '—'}
              </dd>
            </div>
            <div className="span-2">
              <dt>Error message</dt>
              <dd>{data.errorMessage ?? '—'}</dd>
            </div>
          </dl>

          <div className="actions">
            {data.status === 'Completed' ? (
              <>
                <button
                  type="button"
                  className="btn primary"
                  onClick={() => downloadMutation.mutate()}
                  disabled={downloadMutation.isPending}
                >
                  {downloadMutation.isPending ? 'Downloading…' : 'Download signed content'}
                </button>
                <p className="note">
                  Content endpoint:{' '}
                  <code>{apiUrl(`/api/v1/signatures/${id}/content`)}</code>
                </p>
              </>
            ) : null}

            {canCancel ? (
              <button
                type="button"
                className="btn danger"
                onClick={() => cancelMutation.mutate()}
                disabled={cancelMutation.isPending}
              >
                {cancelMutation.isPending ? 'Cancelling…' : 'Cancel signature'}
              </button>
            ) : null}
          </div>

          {cancelError ? <p className="error-text">{cancelError}</p> : null}
          {downloadMutation.error ? (
            <p className="error-text">
              {downloadMutation.error instanceof ApiError
                ? `${downloadMutation.error.errorCode ?? 'ERROR'}: ${downloadMutation.error.message}`
                : downloadMutation.error.message}
            </p>
          ) : null}

          {data.status === 'Completed' ? (
            <section className="report-wrap">
              {verificationQuery.isLoading ? (
                <p className="muted">Verifying signed document…</p>
              ) : null}
              {verificationQuery.error ? (
                <p className="error-text">
                  {verificationQuery.error instanceof ApiError
                    ? `${verificationQuery.error.errorCode ?? 'ERROR'}: ${verificationQuery.error.message}`
                    : verificationQuery.error instanceof Error
                      ? verificationQuery.error.message
                      : 'Verification failed.'}
                </p>
              ) : null}
              {verificationQuery.data ? (
                <VerificationReportPanel report={verificationQuery.data} />
              ) : null}
            </section>
          ) : null}
        </>
      ) : null}
    </section>
  )
}
