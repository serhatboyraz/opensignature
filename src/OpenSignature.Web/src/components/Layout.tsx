import { NavLink, Outlet } from 'react-router-dom'

const GITHUB_URL = 'https://github.com/serhatboyraz/opensignature'
const DOCS_URL = 'https://github.com/serhatboyraz/opensignature/tree/main/docs'

const primaryNav = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/signatures', label: 'Signatures' },
  { to: '/certificates', label: 'Certificates' },
  { to: '/providers', label: 'Providers' },
  { to: '/verify', label: 'Verify' },
]

export function Layout() {
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
      </header>
      <main className="app-main">
        <Outlet />
      </main>
      <footer className="app-footer">
        <nav className="footer-nav" aria-label="Project">
          <a href={GITHUB_URL} target="_blank" rel="noreferrer">
            github.com/serhatboyraz/opensignature
          </a>
          <a href={DOCS_URL} target="_blank" rel="noreferrer">
            Documentation
          </a>
        </nav>
      </footer>
    </div>
  )
}
