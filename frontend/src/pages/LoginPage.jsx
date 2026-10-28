import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, Chip, Link, Stack, TextField, Typography } from '@mui/material';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../utils/format';

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
  const [email, setEmail] = useState('manager@acme.test');
  const [password, setPassword] = useState('Nexora#2026');
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
    } finally {
      setBusy(false);
    }
  };

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, bgcolor: 'background.default' }}>
      <Card sx={{ width: '100%', maxWidth: 420 }}>
        <CardContent component="form" onSubmit={submit} sx={{ p: 4 }}>
          <Stack spacing={2.5}>
            <Box>
              <Typography variant="h5">Nexora</Typography>
              <Typography variant="body2" color="text.secondary">Çok şirketli işlem ve ödeme yönetimi</Typography>
            </Box>
            {error && <Alert severity="error">{errorMessage(error)}</Alert>}
            <TextField label="E-posta" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required />
            <TextField label="Parola" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
            <Button type="submit" variant="contained" size="large" disabled={busy}>Giriş yap</Button>
            <Link component={RouterLink} to="/forgot-password" variant="body2" sx={{ alignSelf: 'center' }}>Parolamı unuttum</Link>
            <Box>
              <Typography variant="caption" color="text.secondary">Demo hesaplar (parola: Nexora#2026)</Typography>
              <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1, mt: 1 }}>
                {DEMO.map(([demoEmail, label]) => (
                  <Chip key={demoEmail} label={label} size="small" onClick={() => setEmail(demoEmail)}
                    color={demoEmail === email ? 'primary' : 'default'} variant={demoEmail === email ? 'filled' : 'outlined'} />
                ))}
              </Stack>
            </Box>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}
