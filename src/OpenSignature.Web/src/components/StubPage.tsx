import { Link } from 'react-router-dom'

interface StubPageProps {
  title: string
  description: string
}

export function StubPage({ title, description }: StubPageProps) {
  return (
    <section className="page">
      <header className="page-header">
        <h1>{title}</h1>
        <p>{description}</p>
      </header>
      <p className="note">
        This area is a placeholder. Use{' '}
        <Link to="/signatures">Signatures</Link>,{' '}
        <Link to="/certificates">Certificates</Link>, or{' '}
        <Link to="/providers">Providers</Link> for live API workflows.
      </p>
    </section>
  )
}
