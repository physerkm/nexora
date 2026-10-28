import { useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, Link, Stack, TextField, Typography } from '@mui/material';
import { api } from '../api/client';
import { errorMessage } from '../utils/format';

function AuthCard({ title, subtitle, onSubmit, children }) {
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, bgcolor: 'background.default' }}>
      <Card sx={{ width: '100%', maxWidth: 420 }}>
        <CardContent component="form" onSubmit={onSubmit} sx={{ p: 4 }}>
          <Stack spacing={2.5}>
            <Box>
              <Typography variant="h5" component="h1">{title}</Typography>
              <Typography variant="body2" color="text.secondary">{subtitle}</Typography>
            </Box>
            {children}
            <Link component={RouterLink} to="/" variant="body2" sx={{ alignSelf: 'center' }}>Giriş ekranına dön</Link>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api('/auth/forgot-password', { method: 'POST', body: { email } });
      setSent(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="Parolamı unuttum" subtitle="E-posta adresinize parola sıfırlama bağlantısı gönderelim." onSubmit={submit}>
      {sent ? (
        <Alert severity="success">
          Bu adres kayıtlıysa birkaç dakika içinde bir parola sıfırlama bağlantısı gelecek. Gelen kutunuzu ve istenmeyen e-postalar klasörünü kontrol edin.
        </Alert>
      ) : (
        <>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="E-posta" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required autoFocus />
          <Button type="submit" variant="contained" size="large" disabled={busy}>Bağlantı gönder</Button>
        </>
      )}
    </AuthCard>
  );
}

const PASSWORD_RULES = 'En az 10 karakter; büyük harf, küçük harf, rakam ve sembol içermeli.';

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const token = params.get('token');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [done, setDone] = useState(false);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const mismatch = confirm.length > 0 && password !== confirm;
  const invalidLink = !token || error?.code === 'invalid_reset_token';

  const submit = async (e) => {
    e.preventDefault();
    if (password !== confirm) return;
    setBusy(true);
    setError(null);
    try {
      await api('/auth/reset-password', { method: 'POST', body: { token, newPassword: password } });
      setDone(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  if (done) {
    return (
      <AuthCard title="Parola değiştirildi" subtitle="Yeni parolanızla giriş yapabilirsiniz.">
        <Alert severity="success">Parolanız güncellendi. Güvenliğiniz için tüm cihazlardaki oturumlar kapatıldı.</Alert>
      </AuthCard>
    );
  }

  if (invalidLink) {
    return (
      <AuthCard title="Bağlantı geçersiz" subtitle="Parola sıfırlama bağlantısı kullanılamıyor.">
        <Alert severity="error">Bu bağlantı geçersiz, daha önce kullanılmış ya da süresi dolmuş. Yeni bir bağlantı isteyin.</Alert>
        <Button component={RouterLink} to="/forgot-password" variant="contained">Yeni bağlantı iste</Button>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Yeni parola belirle" subtitle={PASSWORD_RULES} onSubmit={submit}>
      {error && <Alert severity="error">{errorMessage(error)}</Alert>}
      <TextField label="Yeni parola" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required autoFocus />
      <TextField label="Yeni parola (tekrar)" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="new-password" required
        error={mismatch} helperText={mismatch ? 'Parolalar aynı değil.' : ' '} />
      <Button type="submit" variant="contained" size="large" disabled={busy || mismatch}>Parolayı kaydet</Button>
    </AuthCard>
  );
}
