import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, refreshSession, session } from '../api/client';

const AuthContext = createContext(null);

// Mirrors the API's authorization policies so the UI only offers what the server will allow.
// The server remains the authority: hiding a button is a convenience, not a security control.
const POLICIES = {
  platformAdmin: ['PlatformAdmin'],
  manageTenant: ['TenantAdmin', 'PlatformAdmin'],
  viewUsers: ['TenantAdmin', 'Manager', 'Auditor', 'PlatformAdmin'],
  createTransactions: ['Operator', 'Manager', 'TenantAdmin', 'PlatformAdmin'],
  reviewTransactions: ['Manager', 'TenantAdmin', 'PlatformAdmin'],
  cancelTransactions: ['Operator', 'Manager', 'TenantAdmin', 'PlatformAdmin'],
  viewReports: ['Manager', 'TenantAdmin', 'Auditor', 'PlatformAdmin'],
  viewAudit: ['Auditor', 'TenantAdmin', 'PlatformAdmin'],
};

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [tenant, setTenantState] = useState(null); // platform admin's selected tenant {id, name}

  const clear = useCallback(() => {
    session.setAccessToken(null);
    session.setTenant(null);
    setTenantState(null);
    setUser(null);
  }, []);

  useEffect(() => {
    session.onExpired(clear);
    // Restore the session from the HttpOnly refresh cookie after a page reload.
    refreshSession()
      .then((res) => setUser(res.user))
      .catch(() => clear())
      .finally(() => setLoading(false));
  }, [clear]);

  const login = useCallback(async (email, password) => {
    const res = await api('/auth/login', { method: 'POST', body: { email, password } });
    session.setAccessToken(res.accessToken);
    setUser(res.user);
    return res.user;
  }, []);

  const logout = useCallback(async () => {
    try {
      await api('/auth/logout', { method: 'POST', body: {} });
    } finally {
      clear();
    }
  }, [clear]);

  const selectTenant = useCallback((next) => {
    session.setTenant(next?.id ?? null);
    setTenantState(next ?? null);
  }, []);

  const value = useMemo(() => {
    const roles = user?.roles ?? [];
    return {
      user,
      loading,
      login,
      logout,
      tenant,
      selectTenant,
      isPlatformAdmin: roles.includes('PlatformAdmin'),
      can: (policy) => POLICIES[policy]?.some((r) => roles.includes(r)) ?? false,
      // Platform admins write only on behalf of a selected tenant.
      canWrite: !roles.includes('PlatformAdmin') || !!tenant,
      scopeKey: tenant?.id ?? user?.tenantId ?? 'all',
    };
  }, [user, loading, login, logout, tenant, selectTenant]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);
