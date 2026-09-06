import { useQueries, useQuery } from '@tanstack/react-query'
import { getProviderHealth, listProviders } from '../api/providers'
import { ApiError } from '../api/types'
import { formatDateTime } from '../lib/format'

export function ProvidersPage() {
  const listQuery = useQuery({
    queryKey: ['providers'],
    queryFn: listProviders,
  })

  const healthQueries = useQueries({
    queries: (listQuery.data ?? []).map((provider) => ({
      queryKey: ['provider-health', provider.id],
      queryFn: () => getProviderHealth(provider.id),
      enabled: Boolean(listQuery.data),
      retry: 1,
    })),
  })

  const listError =
    listQuery.error instanceof ApiError
      ? `${listQuery.error.errorCode ?? 'ERROR'}: ${listQuery.error.message}`
      : listQuery.error instanceof Error
        ? listQuery.error.message
        : null

  return (
    <section className="page">
      <header className="page-header">
        <h1>Providers</h1>
        <p>Configured signing providers and live health checks. USB tokens appear as SmartCard after PKCS#11 middleware is installed (Development auto-detects well-known vendor libraries).</p>
      </header>

      {listQuery.isLoading ? <p className="muted">Loading providers…</p> : null}
      {listError ? <p className="error-text">{listError}</p> : null}

      {listQuery.data && listQuery.data.length === 0 ? (
        <p className="empty">No signing providers are registered.</p>
      ) : null}

      {listQuery.data && listQuery.data.length > 0 ? (
        <ul className="provider-list">
          {listQuery.data.map((provider, index) => {
            const health = healthQueries[index]
            const healthError =
              health?.error instanceof ApiError
                ? health.error.errorCode ?? health.error.message
                : health?.error instanceof Error
                  ? health.error.message
                  : null

            return (
              <li key={provider.id} className="provider-row">
                <div>
                  <h2>{provider.name}</h2>
                  <p className="muted">
                    <span className="mono">{provider.id}</span> · {provider.providerType}
                  </p>
                </div>
                <div className="provider-health">
                  {health?.isLoading ? <span className="muted">Checking health…</span> : null}
                  {health?.data ? (
                    <>
                      <span
                        className={
                          health.data.isHealthy ? 'status-badge tone-ok' : 'status-badge tone-bad'
                        }
                      >
                        {health.data.state}
                      </span>
                      <p className="muted small">
                        Checked {formatDateTime(health.data.checkedAt)}
                        {health.data.detail ? ` · ${health.data.detail}` : ''}
                      </p>
                    </>
                  ) : null}
                  {healthError ? <p className="error-text">{healthError}</p> : null}
                </div>
              </li>
            )
          })}
        </ul>
      ) : null}
    </section>
  )
}
