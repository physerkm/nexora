import { useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Stack, TextField, Typography } from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBackRounded';
import CheckIcon from '@mui/icons-material/CheckCircleRounded';
import CircleIcon from '@mui/icons-material/RadioButtonUncheckedRounded';
import { api } from '../api/client';
import AuthLayout from '../components/AuthLayout';
import PasswordField from '../components/PasswordField';
import { errorMessage } from '../utils/format';

const BackToLogin = (
  <Link component={RouterLink} to="/" variant="body2" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75 }}>
    <ArrowBackIcon sx={{ fontSize: 16 }} /> Giriş ekranına dön
  </Link>
);

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
    <AuthLayout
      title="Parolamı unuttum"
      subtitle="Hesabınıza kayıtlı e-posta adresini girin; parola sıfırlama bağlantısı gönderelim."
      onSubmit={sent ? undefined : submit}
      footer={BackToLogin}
    >
      {sent ? (
        <Alert severity="success">
          Bu adres kayıtlıysa birkaç dakika içinde bir parola sıfırlama bağlantısı gelecek. Gelen kutunuzu ve istenmeyen
          e-postalar klasörünü kontrol edin.
        </Alert>
      ) : (
        <>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="E-posta" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required autoFocus />
          <Button type="submit" variant="contained" size="large" loading={busy}>Bağlantı gönder</Button>
        </>
      )}
    </AuthLayout>
  );
}

// Mirrors the API's Identity password options; the server stays the authority.
const PASSWORD_RULES = [
  ['En az 10 karakter', (p) => p.length >= 10],
  ['Büyük harf', (p) => /\p{Lu}/u.test(p)],
  ['Küçük harf', (p) => /\p{Ll}/u.test(p)],
  ['Rakam', (p) => /\d/.test(p)],
  ['Sembol', (p) => /[^\p{L}\d]/u.test(p)],
];

function PasswordChecklist({ password }) {
  return (
    <Box component="ul" aria-label="Parola kuralları" sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 0.75 }}>
      {PASSWORD_RULES.map(([label, test]) => {
        const ok = test(password);
        return (
          <Box component="li" key={label} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
            {ok ? <CheckIcon sx={{ fontSize: 16, color: 'success.main' }} /> : <CircleIcon sx={{ fontSize: 16, color: 'text.disabled' }} />}
            <Typography variant="caption" color={ok ? 'text.primary' : 'text.secondary'}>{label}</Typography>
          </Box>
        );
      })}
    </Box>
  );
}

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
      <AuthLayout title="Parola değiştirildi" subtitle="Yeni parolanızla giriş yapabilirsiniz.">
        <Alert severity="success">Parolanız güncellendi. Güvenliğiniz için tüm cihazlardaki oturumlar kapatıldı.</Alert>
        <Button component={RouterLink} to="/" variant="contained" size="large">Giriş yap</Button>
      </AuthLayout>
    );
  }

  if (invalidLink) {
    return (
      <AuthLayout title="Bağlantı geçersiz" subtitle="Parola sıfırlama bağlantısı kullanılamıyor." footer={BackToLogin}>
        <Alert severity="error">Bu bağlantı geçersiz, daha önce kullanılmış ya da süresi dolmuş. Yeni bir bağlantı isteyin.</Alert>
        <Button component={RouterLink} to="/forgot-password" variant="contained" size="large">Yeni bağlantı iste</Button>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="Yeni parola belirle" subtitle="Hesabınız için güçlü bir parola seçin." onSubmit={submit} footer={BackToLogin}>
      {error && <Alert severity="error">{errorMessage(error)}</Alert>}
      <Stack spacing={1.5}>
        <PasswordField
          label="Yeni parola" value={password} onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password" required autoFocus
        />
        <PasswordChecklist password={password} />
      </Stack>
      <PasswordField
        label="Yeni parola (tekrar)" value={confirm} onChange={(e) => setConfirm(e.target.value)}
        autoComplete="new-password" required error={mismatch} helperText={mismatch ? 'Parolalar aynı değil.' : ' '}
      />
      <Button type="submit" variant="contained" size="large" loading={busy} disabled={mismatch}>Parolayı kaydet</Button>
    </AuthLayout>
  );
}
