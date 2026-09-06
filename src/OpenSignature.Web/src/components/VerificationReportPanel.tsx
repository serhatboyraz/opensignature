import { formatDateTime } from '../lib/format'
import type { SignatureVerificationReport } from '../api/verification'
import { reasonCodeLabel } from '../lib/verificationLabels'
import { StatusBadge } from './StatusBadge'

interface VerificationReportPanelProps {
  report: SignatureVerificationReport
}

export function VerificationReportPanel({ report }: VerificationReportPanelProps) {
  return (
    <article className="report-card" aria-label="Verification report">
      <header className="report-card-head">
        <div>
          <h2>Verification report</h2>
          <p className="muted small">
            {report.format ?? 'Unknown format'} · {report.source} · checked{' '}
            {formatDateTime(report.checkedAt)}
          </p>
        </div>
        <StatusBadge status={report.overallStatus} />
      </header>

      <dl className="detail-grid">
        <div>
          <dt>Overall</dt>
          <dd>{report.isValid ? 'Signature checks passed' : 'Signature checks did not pass'}</dd>
        </div>
        {report.signatureId ? (
          <div>
            <dt>Signature id</dt>
            <dd className="mono wrap">{report.signatureId}</dd>
          </div>
        ) : null}
        <div className="span-2">
          <dt>Reason codes</dt>
          <dd>
            <ReasonCodeList codes={report.reasonCodes} />
          </dd>
        </div>
        {report.detail ? (
          <div className="span-2">
            <dt>Detail</dt>
            <dd>{report.detail}</dd>
          </div>
        ) : null}
      </dl>

      {report.signature ? (
        <section className="report-section">
          <h3>Cryptographic signature</h3>
          <dl className="detail-grid">
            <div>
              <dt>Crypto valid</dt>
              <dd className={report.signature.cryptoValid ? 'tone-ok' : 'tone-bad'}>
                {report.signature.cryptoValid ? 'Yes' : 'No'}
              </dd>
            </div>
            <div>
              <dt>Signer</dt>
              <dd>{report.signature.signerSubject ?? '—'}</dd>
            </div>
            <div className="span-2">
              <dt>Signer thumbprint</dt>
              <dd className="mono wrap">{report.signature.signerThumbprint ?? '—'}</dd>
            </div>
            <div className="span-2">
              <dt>Signature codes</dt>
              <dd>
                <ReasonCodeList codes={report.signature.reasonCodes} />
              </dd>
            </div>
          </dl>
        </section>
      ) : null}

      {report.certificate ? (
        <section className="report-section">
          <h3>Certificate path</h3>
          <dl className="detail-grid">
            <div>
              <dt>Certificate valid</dt>
              <dd className={report.certificate.isValid ? 'tone-ok' : 'tone-bad'}>
                {report.certificate.isValid ? 'Yes' : 'No'}
              </dd>
            </div>
            <div>
              <dt>Subject</dt>
              <dd>{report.certificate.subject ?? '—'}</dd>
            </div>
            <div>
              <dt>Issuer</dt>
              <dd>{report.certificate.issuer ?? '—'}</dd>
            </div>
            <div>
              <dt>Validity</dt>
              <dd>
                {formatDateTime(report.certificate.notBefore)} →{' '}
                {formatDateTime(report.certificate.notAfter)}
              </dd>
            </div>
            <div className="span-2">
              <dt>Thumbprint</dt>
              <dd className="mono wrap">{report.certificate.thumbprint ?? '—'}</dd>
            </div>
            <div className="span-2">
              <dt>Certificate codes</dt>
              <dd>
                <ReasonCodeList codes={report.certificate.reasonCodes} />
              </dd>
            </div>
            {report.certificate.chainStatus.length > 0 ? (
              <div className="span-2">
                <dt>Chain status</dt>
                <dd>
                  <ul className="code-list">
                    {report.certificate.chainStatus.map((item) => (
                      <li key={item} className="mono small">
                        {item}
                      </li>
                    ))}
                  </ul>
                </dd>
              </div>
            ) : null}
          </dl>

          {report.certificate.revocation ? (
            <dl className="detail-grid">
              <div>
                <dt>Revocation</dt>
                <dd>{report.certificate.revocation.status}</dd>
              </div>
              <div>
                <dt>Source</dt>
                <dd>{report.certificate.revocation.source}</dd>
              </div>
              {report.certificate.revocation.detail ? (
                <div className="span-2">
                  <dt>Revocation detail</dt>
                  <dd>{report.certificate.revocation.detail}</dd>
                </div>
              ) : null}
            </dl>
          ) : null}
        </section>
      ) : null}

      <p className="note">{report.limitations}</p>
    </article>
  )
}

function ReasonCodeList({ codes }: { codes: string[] }) {
  if (!codes.length) {
    return <span className="muted">—</span>
  }

  return (
    <ul className="code-list">
      {codes.map((code) => (
        <li key={code}>
          <code>{code}</code>
          <span className="muted"> {reasonCodeLabel(code)}</span>
        </li>
      ))}
    </ul>
  )
}
