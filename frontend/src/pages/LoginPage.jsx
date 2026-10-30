import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Divider, Link, Stack, TextField, Typography } from '@mui/material';
import { useAuth } from '../auth/AuthContext';
import AuthLayout from '../components/AuthLayout';
import PasswordField from '../components/PasswordField';
import { errorMessage } from '../utils/format';

// Demo shortcuts match the seeded demo data. Build with VITE_DEMO_ACCOUNTS=false for a real deployment.
const SHOW_DEMO = import.meta.env.VITE_DEMO_ACCOUNTS !== 'false';
const DEMO_PASSWORD = 'Nexora#2026';
const DEMO = [
  ['admin@nexora.io', 'Platform yöneticisi'],
  ['admin@acme.test', 'Acme yönetici'],
  ['manager@acme.test', 'Acme müdür'],
  ['operator@acme.test', 'Acme operatör'],
  ['auditor@acme.test', 'Acme denetçi'],
  ['manager@globex.test', 'Globex müdür'],
];

export default function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState(SHOW_DEMO ? 'manager@acme.test' : '');
  const [password, setPassword] = useState(SHOW_DEMO ? DEMO_PASSWORD : '');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };

  const pickDemo = (demoEmail) => {
    setEmail(demoEmail);
    setPassword(DEMO_PASSWORD);
    setError(null);
  };

  return (
    <AuthLayout
      title="Giriş yapın"
      subtitle="Devam etmek için Nexora hesabınızla oturum açın."
      onSubmit={submit}
      footer={SHOW_DEMO && (
        <Box>
          <Divider sx={{ mb: 2, typography: 'caption', color: 'text.secondary' }}>Demo hesapları · parola {DEMO_PASSWORD}</Divider>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 1fr 1fr' }, gap: 1 }}>
            {DEMO.map(([demoEmail, label]) => {
              const selected = demoEmail === email;
              return (
                <Button
                  key={demoEmail}
                  size="small"
                  variant="outlined"
                  color={selected ? 'primary' : 'inherit'}
                  aria-pressed={selected}
                  onClick={() => pickDemo(demoEmail)}
                  sx={{ justifyContent: 'flex-start', fontWeight: selected ? 600 : 500, bgcolor: selected ? 'action.selected' : undefined }}
                >
                  {label}
                </Button>
              );
            })}
          </Box>
        </Box>
      )}
    >
      {error && <Alert severity="error">{errorMessage(error)}</Alert>}
      <TextField
        label="E-posta" type="email" value={email} onChange={(e) => setEmail(e.target.value)}
        autoComplete="username" required autoFocus={!SHOW_DEMO}
      />
      <Stack spacing={1}>
        <PasswordField
          label="Parola" value={password} onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password" required
        />
        <Typography variant="body2" sx={{ alignSelf: 'flex-end' }}>
          <Link component={RouterLink} to="/forgot-password">Parolamı unuttum</Link>
        </Typography>
      </Stack>
      <Button type="submit" variant="contained" size="large" loading={busy}>Giriş yap</Button>
    </AuthLayout>
  );
}
