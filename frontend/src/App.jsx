import { Box, CircularProgress } from '@mui/material';
import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import TransactionsPage from './pages/TransactionsPage';
import BranchesPage from './pages/BranchesPage';
import UsersPage from './pages/UsersPage';
import FeeRulesPage from './pages/FeeRulesPage';
import AuditLogsPage from './pages/AuditLogsPage';
import TenantsPage from './pages/TenantsPage';
import SettingsPage from './pages/SettingsPage';
import { ForgotPasswordPage, ResetPasswordPage } from './pages/PasswordResetPages';

function Guard({ policy, children }) {
  const { can } = useAuth();
  return !policy || can(policy) ? children : <Navigate to="/transactions" replace />;
}

export default function App() {
  const { user, loading, can } = useAuth();

  if (loading) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', height: '100vh' }}>
        <CircularProgress />
      </Box>
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
