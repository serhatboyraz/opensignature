import { NavLink, Outlet } from 'react-router-dom'
import { getApiBaseUrl, getTenantId } from '../lib/config'

const primaryNav = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/signatures', label: 'Signatures' },
  { to: '/certificates', label: 'Certificates' },
  { to: '/providers', label: 'Providers' },
  { to: '/verify', label: 'Verify' },
]

const secondaryNav = [
  { to: '/jobs', label: 'Jobs' },
  { to: '/audit', label: 'Audit' },
  { to: '/settings', label: 'Settings' },
]

export function Layout() {
  const apiBase = getApiBaseUrl() || '(Vite proxy)'
  const tenantId = getTenantId() ?? 'default'

  return (
    <div className="app-frame">
      <header className="app-header">
        <div className="brand-block">
          <p className="brand-mark">OpenSignature</p>
          <p className="brand-tagline">Asynchronous digital signing for PAdES, XAdES, CAdES, and ASiC.</p>
        </div>
        <nav className="primary-nav" aria-label="Primary">
          {primaryNav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <nav className="secondary-nav" aria-label="Secondary">
          {secondaryNav.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => (isActive ? 'nav-link muted active' : 'nav-link muted')}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <p className="env-meta">
          API {apiBase} · tenant {tenantId}
        </p>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
