import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError } from '../api/types'
import type { SignatureFormat } from '../api/types'
import { verifyUploadedSignature } from '../api/verification'
import { VerificationReportPanel } from '../components/VerificationReportPanel'

const FORMATS: SignatureFormat[] = ['PAdES', 'XAdES', 'CAdES', 'ASiC_S', 'ASiC_E']

export function VerifyPage() {
  const [file, setFile] = useState<File | null>(null)
  const [originalFile, setOriginalFile] = useState<File | null>(null)
  const [format, setFormat] = useState<SignatureFormat>('CAdES')
  const [formError, setFormError] = useState<string | null>(null)

  const verifyMutation = useMutation({
    mutationFn: verifyUploadedSignature,
    onSuccess: () => setFormError(null),
    onError: (error: unknown) => {
      if (error instanceof ApiError) {
        setFormError(error.errorCode ? `${error.errorCode}: ${error.message}` : error.message)
        return
      }
      setFormError(error instanceof Error ? error.message : 'Verification failed')
    },
  })

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!file) {
      setFormError('Choose a signed file to verify.')
      return
    }

    verifyMutation.mutate({
      file,
      format,
      originalFile,
    })
  }

  return (
    <section className="page">
      <header className="page-header">
        <h1>Verify signature</h1>
        <p>
          Upload a signed CAdES, XAdES, PAdES, or ASiC container to run cryptographic and
          certificate checks. Detached CAdES also needs the original file.
        </p>
      </header>

      <div className="split">
        <form className="stack-form" onSubmit={onSubmit}>
          <h2>Signed document</h2>
          <label className="field">
            <span>Signed file</span>
            <input
              type="file"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <div className="field-row">
            <label className="field">
              <span>Format</span>
              <select
                value={format}
                onChange={(event) => setFormat(event.target.value as SignatureFormat)}
              >
                {FORMATS.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <label className="field">
            <span>Original file (detached CAdES only)</span>
            <input
              type="file"
              onChange={(event) => setOriginalFile(event.target.files?.[0] ?? null)}
            />
          </label>
          {formError ? <p className="error-text">{formError}</p> : null}
          <button type="submit" className="btn primary" disabled={verifyMutation.isPending}>
            {verifyMutation.isPending ? 'Verifying…' : 'Verify signature'}
          </button>
        </form>

        <div className="list-panel">
          {verifyMutation.data ? (
            <VerificationReportPanel report={verifyMutation.data} />
          ) : (
            <p className="empty">Verification results will appear here.</p>
          )}
        </div>
      </div>
    </section>
  )
}
