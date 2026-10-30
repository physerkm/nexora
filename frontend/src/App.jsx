import { lazy } from 'react';
import { CircularProgress, Stack } from '@mui/material';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import { BrandMark } from './components/Brand';
import LoginPage from './pages/LoginPage';
import { ForgotPasswordPage, ResetPasswordPage } from './pages/PasswordResetPages';

// Signed-in pages load on first visit, so the sign-in screen does not wait for charts and data grids.
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const TransactionsPage = lazy(() => import('./pages/TransactionsPage'));
const BranchesPage = lazy(() => import('./pages/BranchesPage'));
const UsersPage = lazy(() => import('./pages/UsersPage'));
const FeeRulesPage = lazy(() => import('./pages/FeeRulesPage'));
const AuditLogsPage = lazy(() => import('./pages/AuditLogsPage'));
const TenantsPage = lazy(() => import('./pages/TenantsPage'));
const SettingsPage = lazy(() => import('./pages/SettingsPage'));

function Guard({ policy, children }) {
  const { can } = useAuth();
  return !policy || can(policy) ? children : <Navigate to="/transactions" replace />;
}

export default function App() {
  const { user, loading, can } = useAuth();

  if (loading) {
    return (
      <Stack spacing={3} sx={{ alignItems: 'center', justifyContent: 'center', height: '100vh', bgcolor: 'background.default' }}>
        <BrandMark size={40} />
        <CircularProgress size={20} thickness={5} aria-label="Yükleniyor" />
      </Stack>
    );
  }

  if (!user) {
    return (
      <Routes>
        <Route path="forgot-password" element={<ForgotPasswordPage />} />
        <Route path="reset-password" element={<ResetPasswordPage />} />
        <Route path="*" element={<LoginPage />} />
      </Routes>
    );
  }

  return (
    <Routes>
      {/* A reset link opened while signed in still works (e.g. resetting another account on a shared computer). */}
      <Route path="reset-password" element={<ResetPasswordPage />} />
      <Route element={<Layout />}>
        <Route index element={<Navigate to={can('viewReports') ? '/dashboard' : '/transactions'} replace />} />
        <Route path="dashboard" element={<Guard policy="viewReports"><DashboardPage /></Guard>} />
        <Route path="transactions" element={<TransactionsPage />} />
        <Route path="branches" element={<BranchesPage />} />
        <Route path="users" element={<Guard policy="viewUsers"><UsersPage /></Guard>} />
        <Route path="fees" element={<FeeRulesPage />} />
        <Route path="audit" element={<Guard policy="viewAudit"><AuditLogsPage /></Guard>} />
        <Route path="tenants" element={<Guard policy="platformAdmin"><TenantsPage /></Guard>} />
        <Route path="settings" element={<Guard policy="manageTenant"><SettingsPage /></Guard>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
