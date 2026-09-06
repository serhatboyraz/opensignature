import { useQuery } from '@tanstack/react-query'
import { listCertificates } from '../api/providers'
import { ApiError } from '../api/types'
import { formatDateTime } from '../lib/format'

export function CertificatesPage() {
  const query = useQuery({
    queryKey: ['certificates'],
    queryFn: listCertificates,
  })

  const error =
    query.error instanceof ApiError
      ? `${query.error.errorCode ?? 'ERROR'}: ${query.error.message}`
      : query.error instanceof Error
        ? query.error.message
        : null

  return (
    <section className="page">
      <header className="page-header">
        <h1>Certificates</h1>
        <p>
          Public certificate metadata from configured signing providers. Private keys are never
          returned or displayed.
        </p>
      </header>

      {query.isLoading ? <p className="muted">Loading certificates…</p> : null}
      {error ? <p className="error-text">{error}</p> : null}

      {query.data && query.data.length === 0 ? (
        <p className="empty">No certificates reported by providers.</p>
      ) : null}

      {query.data && query.data.length > 0 ? (
        <div className="table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Subject</th>
                <th>Thumbprint</th>
                <th>Provider</th>
                <th>Valid</th>
                <th>Not after</th>
                <th>Can sign</th>
              </tr>
            </thead>
            <tbody>
              {query.data.map((cert) => (
                <tr key={`${cert.providerId}-${cert.thumbprint}`}>
                  <td>
                    <div>{cert.friendlyName ?? cert.subject}</div>
                    {cert.friendlyName ? (
                      <div className="muted small">{cert.subject}</div>
                    ) : null}
                  </td>
                  <td className="mono small">{cert.thumbprint}</td>
                  <td>{cert.providerId}</td>
                  <td>{cert.isCurrentlyValid ? 'Yes' : 'No'}</td>
                  <td>{formatDateTime(cert.notAfter)}</td>
                  <td>{cert.canSign ? 'Yes' : 'No'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  )
}
