import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import './Admin.css'

type User = { id: string; email: string; displayName: string }
type TenantRole = { id: string; name: string; isSystem: boolean; permissionCodes: string[] }
type TenantMemberRole = { id: string; name: string }
type Member = {
  membershipId: string
  userId: string
  email: string
  displayName: string
  status: 'Active' | 'Invited' | 'Suspended'
  joinedAtUtc: string
  roles: TenantMemberRole[]
}
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
  const [tenantLoaded, setTenantLoaded] = useState(false)
  const [memberEmail, setMemberEmail] = useState('')
  const [members, setMembers] = useState<Member[]>([])
  const [roles, setRoles] = useState<TenantRole[]>([])
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
      setMembers([])
      setRoles([])
      setTenantId('')
      setTenantLoaded(false)
      setMessage('You have signed out.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sign out failed.')
    } finally {
      setBusy(false)
    }
  }

  async function loadMemberships(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await loadTenantData(tenantId.trim())
  }

  async function loadTenantData(selectedTenantId: string) {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const headers = { 'X-Tenant-ID': selectedTenantId }
      const [memberResult, roleResult] = await Promise.all([
        api<Member[]>('/api/v1/tenant/members', { headers }),
        api<TenantRole[]>('/api/v1/tenant/roles', { headers }),
      ])
      setMembers(memberResult)
      setRoles(roleResult)
      setTenantLoaded(true)
      setMessage(`${memberResult.length} member${memberResult.length === 1 ? '' : 's'} loaded.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Tenant data could not be loaded.')
      setMembers([])
      setRoles([])
      setTenantLoaded(false)
    } finally {
      setBusy(false)
    }
  }

  async function addMember(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const token = await getCsrfToken()
      await api('/api/v1/tenant/memberships', {
        method: 'POST',
        headers: { 'X-CSRF-TOKEN': token, 'X-Tenant-ID': tenantId.trim() },
        body: JSON.stringify({ email: memberEmail.trim() }),
      })
      setMemberEmail('')
      setMessage('Member added to the tenant.')
      await refreshTenantData()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The member could not be added.')
    } finally {
      setBusy(false)
    }
  }

  async function updateMemberStatus(member: Member) {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const token = await getCsrfToken()
      const status = member.status === 'Active' ? 'Suspended' : 'Active'
      await api(`/api/v1/tenant/memberships/${member.membershipId}/status`, {
        method: 'PUT',
        headers: { 'X-CSRF-TOKEN': token, 'X-Tenant-ID': tenantId.trim() },
        body: JSON.stringify({ status }),
      })
      setMessage(`${member.email} is now ${status.toLowerCase()}.`)
      await refreshTenantData()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Membership status could not be updated.')
    } finally {
      setBusy(false)
    }
  }

  async function toggleRole(member: Member, role: TenantRole, assigned: boolean) {
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const token = await getCsrfToken()
      const path = `/api/v1/tenant/memberships/${member.membershipId}/roles/${role.id}`
      await api(path, {
        method: assigned ? 'DELETE' : 'POST',
        headers: { 'X-CSRF-TOKEN': token, 'X-Tenant-ID': tenantId.trim() },
      })
      setMessage(`${role.name} ${assigned ? 'removed from' : 'assigned to'} ${member.email}.`)
      await refreshTenantData()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The role assignment could not be updated.')
    } finally {
      setBusy(false)
    }
  }

  async function refreshTenantData() {
    const headers = { 'X-Tenant-ID': tenantId.trim() }
    const [memberResult, roleResult] = await Promise.all([
      api<Member[]>('/api/v1/tenant/members', { headers }),
      api<TenantRole[]>('/api/v1/tenant/roles', { headers }),
    ])
    setMembers(memberResult)
    setRoles(roleResult)
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
                <div className="card-heading"><div><h2 id="members-title">Tenant members</h2><p>Member changes are verified by the API for the selected tenant.</p></div><span className="count-badge">{members.length}</span></div>
                <form className="tenant-form" onSubmit={loadMemberships}>
                  <div className="field"><label htmlFor="tenant-id">Tenant ID</label><input id="tenant-id" inputMode="text" autoComplete="off" placeholder="Paste a tenant UUID" value={tenantId} onChange={event => { setTenantId(event.target.value); setTenantLoaded(false) }} required aria-describedby="tenant-hint" /><small id="tenant-hint">The API verifies your active membership and permissions.</small></div>
                  <button className="primary-button" type="submit" disabled={busy}>Load tenant <span aria-hidden="true">→</span></button>
                </form>
                {tenantLoaded && <>
                  <form className="add-member-form" onSubmit={addMember}>
                    <div className="field"><label htmlFor="member-email">Add existing account</label><input id="member-email" type="email" autoComplete="email" placeholder="person@example.com" value={memberEmail} onChange={event => setMemberEmail(event.target.value)} required aria-describedby="member-email-hint" /><small id="member-email-hint">The person must already have an account. Email invitations are not available yet.</small></div>
                    <button className="primary-button" type="submit" disabled={busy}>Add member <span aria-hidden="true">+</span></button>
                  </form>
                  {members.length === 0 ? <p className="empty-state">No members belong to this tenant yet.</p> : (
                    <div className="member-list">
                      {members.map(member => (
                        <article className="member-row" key={member.membershipId} aria-labelledby={`member-${member.membershipId}`}>
                          <div className="member-summary">
                            <span className="avatar" aria-hidden="true">{member.displayName.slice(0, 1).toUpperCase()}</span>
                            <div><h3 id={`member-${member.membershipId}`}>{member.displayName}</h3><p>{member.email}</p><small className="mono">{member.membershipId}</small></div>
                            <span className={`table-status ${member.status === 'Suspended' ? 'status-suspended' : ''}`}>{member.status}</span>
                          </div>
                          <div className="member-actions">
                            <fieldset className="role-assignments">
                              <legend>Tenant roles</legend>
                              {roles.length === 0 ? <p className="empty-state">No roles are available.</p> : roles.map(role => {
                                const assigned = member.roles.some(item => item.id === role.id)
                                return <label key={role.id}><input type="checkbox" checked={assigned} disabled={busy || member.status !== 'Active'} onChange={() => toggleRole(member, role, assigned)} />{role.name}</label>
                              })}
                            </fieldset>
                            <button className="quiet-button member-status-button" type="button" onClick={() => updateMemberStatus(member)} disabled={busy || member.userId === user.id}>{member.status === 'Active' ? 'Deactivate' : 'Reactivate'}</button>
                          </div>
                          <p className="member-joined">Joined {new Date(member.joinedAtUtc).toLocaleDateString()}</p>
                        </article>
                      ))}
                    </div>
                  )}
                </>}
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
