import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type User = { id: string; email: string; displayName: string }
type Membership = { id: string; userId: string; status: string; joinedAtUtc: string }
type Problem = { title?: string; errors?: Record<string, string[]> }
type Csrf = { requestToken: string }

const apiBase = import.meta.env.VITE_API_URL ?? 'http://localhost:5247'

async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init.headers },
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as Problem
    const fieldMessage = Object.values(problem.errors ?? {}).flat()[0]
    throw new Error(fieldMessage ?? problem.title ?? `Request failed (${response.status})`)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

async function getCsrfToken() {
  return (await api<Csrf>('/api/v1/auth/csrf')).requestToken
}

function App() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [isRegistering, setIsRegistering] = useState(false)
  const [user, setUser] = useState<User | null>(null)
  const [tenantId, setTenantId] = useState('')
  const [memberships, setMemberships] = useState<Membership[]>([])
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api<User>('/api/v1/auth/me').then(setUser).catch(() => undefined)
  }, [])

  async function handleAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setMessage('')
    try {
      if (isRegistering) {
        const registerToken = await getCsrfToken()
        await api('/api/v1/auth/register', {
          method: 'POST',
          headers: { 'X-CSRF-TOKEN': registerToken },
          body: JSON.stringify({ email, displayName, password }),
        })
      }
      const loginToken = await getCsrfToken()
      const signedIn = await api<User>('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': loginToken },
        body: JSON.stringify({ email, password }),
      })
      setUser(signedIn)
      setMessage(`Signed in as ${signedIn.email}.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The request could not be completed.')
    } finally {
      setBusy(false)
    }
  }

  async function handleLogout() {
    setBusy(true)
    setError('')
    try {
      const token = await getCsrfToken()
      await api('/api/v1/auth/logout', { method: 'POST', headers: { 'X-CSRF-TOKEN': token } })
      setUser(null)
      setMemberships([])
      setMessage('You have signed out.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sign out failed.')
    } finally {
      setBusy(false)
    }
  }

  async function loadMemberships(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const result = await api<Membership[]>('/api/v1/tenant/memberships', {
        headers: { 'X-Tenant-ID': tenantId.trim() },
      })
      setMemberships(result)
      setMessage(`${result.length} membership${result.length === 1 ? '' : 's'} loaded.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Memberships could not be loaded.')
      setMemberships([])
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="shell">
      <aside className="rail" aria-label="Application navigation">
        <a className="brand" href="#home" aria-label="Common Web Platform home">
          <span className="brand-mark" aria-hidden="true">C</span>
          <span>Common Web<br />Platform</span>
        </a>
        <div className="rail-label">WORKSPACE</div>
        <a className="nav-item active" href="#access" aria-current="page"><span aria-hidden="true">◫</span> Access</a>
        <div className="rail-footer"><span className="status-dot" /> Local API<br /><small>Development</small></div>
      </aside>

      <main id="home" className="main-area">
        <header className="topbar">
          <div><span className="eyebrow">PLATFORM CONSOLE</span><span className="divider">/</span><span>Identity &amp; access</span></div>
          {user && <button className="quiet-button" type="button" onClick={handleLogout} disabled={busy}>Sign out</button>}
        </header>

        <section id="access" className="content">
          <div className="heading-row">
            <div>
              <p className="eyebrow accent">ACCOUNT</p>
              <h1>{user ? 'Your workspace access' : 'Welcome back'}</h1>
              <p className="subtitle">A small shell connected to the Common Web Platform API.</p>
            </div>
            <span className="api-pill"><span className="status-dot" /> API connected</span>
          </div>

          {error && <div className="notice error" role="alert">{error}</div>}
          {message && <div className="notice success" role="status" aria-live="polite">{message}</div>}

          {!user ? (
            <section className="card auth-card" aria-labelledby="auth-title">
              <div className="card-heading">
                <div><h2 id="auth-title">{isRegistering ? 'Create local account' : 'Sign in'}</h2><p>Use your development account to continue.</p></div>
                <span className="icon-tile" aria-hidden="true">↗</span>
              </div>
              <form onSubmit={handleAuth}>
                {isRegistering && <div className="field"><label htmlFor="display-name">Display name</label><input id="display-name" autoComplete="name" value={displayName} onChange={event => setDisplayName(event.target.value)} required maxLength={120} /></div>}
                <div className="field"><label htmlFor="email">Email address</label><input id="email" type="email" autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} required /></div>
                <div className="field"><div className="label-row"><label htmlFor="password">Password</label>{!isRegistering && <span>12+ characters</span>}</div><input id="password" type="password" autoComplete={isRegistering ? 'new-password' : 'current-password'} value={password} onChange={event => setPassword(event.target.value)} required minLength={12} /></div>
                <button className="primary-button full-width" type="submit" disabled={busy}>{busy ? 'Please wait…' : isRegistering ? 'Create account and sign in' : 'Sign in'} <span aria-hidden="true">→</span></button>
              </form>
              <p className="switch-mode">{isRegistering ? 'Already have an account?' : 'New to this local workspace?'} <button type="button" onClick={() => { setIsRegistering(!isRegistering); setError('') }}>{isRegistering ? 'Sign in' : 'Create an account'}</button></p>
              <p className="helper-note">Local registration is available only while the API runs in Development.</p>
            </section>
          ) : (
            <div className="dashboard-grid">
              <section className="card profile-card" aria-labelledby="profile-title">
                <div className="card-heading"><div><h2 id="profile-title">Signed-in account</h2><p>Authenticated by the platform session cookie.</p></div><span className="avatar" aria-hidden="true">{user.displayName.slice(0, 1).toUpperCase()}</span></div>
                <dl className="profile-list"><div><dt>Name</dt><dd>{user.displayName}</dd></div><div><dt>Email</dt><dd>{user.email}</dd></div><div><dt>User ID</dt><dd className="mono">{user.id}</dd></div></dl>
              </section>

              <section className="card membership-card" aria-labelledby="members-title">
                <div className="card-heading"><div><h2 id="members-title">Tenant memberships</h2><p>Membership access is checked by the API for each tenant.</p></div><span className="count-badge">{memberships.length}</span></div>
                <form className="tenant-form" onSubmit={loadMemberships}>
                  <div className="field"><label htmlFor="tenant-id">Tenant ID</label><input id="tenant-id" inputMode="text" autoComplete="off" placeholder="Paste a tenant UUID" value={tenantId} onChange={event => setTenantId(event.target.value)} required aria-describedby="tenant-hint" /><small id="tenant-hint">The API verifies your active membership before returning data.</small></div>
                  <button className="primary-button" type="submit" disabled={busy}>Load memberships <span aria-hidden="true">→</span></button>
                </form>
                <div className="table-wrap">
                  <table>
                    <caption className="sr-only">Memberships for the selected tenant</caption>
                    <thead><tr><th scope="col">Membership ID</th><th scope="col">Status</th><th scope="col">Joined</th></tr></thead>
                    <tbody>{memberships.length === 0 ? <tr><td className="empty-cell" colSpan={3}>No memberships loaded yet.</td></tr> : memberships.map(item => <tr key={item.id}><td className="mono">{item.id}</td><td><span className="table-status">{item.status}</span></td><td>{new Date(item.joinedAtUtc).toLocaleDateString()}</td></tr>)}</tbody>
                  </table>
                </div>
              </section>
            </div>
          )}
          <footer className="page-footer">Common Web Platform <span>•</span> Local development evaluation</footer>
        </section>
      </main>
    </div>
  )
}

export default App
