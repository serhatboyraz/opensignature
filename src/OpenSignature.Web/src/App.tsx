import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { StubPage } from './components/StubPage'
import { CertificatesPage } from './pages/CertificatesPage'
import { ProvidersPage } from './pages/ProvidersPage'
import { VerifyPage } from './pages/VerifyPage'
import { SignatureDashboardPage } from './pages/SignatureDashboardPage'
import { SignatureDetailPage } from './pages/SignatureDetailPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="dashboard" element={<SignatureDashboardPage />} />
          <Route path="signatures" element={<Navigate to="/dashboard" replace />} />
          <Route path="signatures/:id" element={<SignatureDetailPage />} />
          <Route path="verify" element={<VerifyPage />} />
          <Route path="certificates" element={<CertificatesPage />} />
          <Route path="providers" element={<ProvidersPage />} />
          <Route
            path="jobs"
            element={
              <StubPage
                title="Jobs"
                description="Worker job inspection UI is planned for a later release."
              />
            }
          />
          <Route
            path="audit"
            element={
              <StubPage
                title="Audit"
                description="Audit trail browsing will appear once audit APIs are exposed to the UI."
              />
            }
          />
          <Route
            path="settings"
            element={
              <StubPage
                title="Settings"
                description="Tenant and environment settings will live here. For now, configure VITE_* env vars."
              />
            }
          />
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
