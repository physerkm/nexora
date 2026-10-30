import { useEffect, useState } from 'react';
import {
  Alert, Box, Button, Card, Checkbox, Divider, FormControlLabel, Grid, IconButton, InputAdornment, Skeleton, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import ShowIcon from '@mui/icons-material/VisibilityOutlined';
import HideIcon from '@mui/icons-material/VisibilityOffOutlined';
import { api } from '../api/client';
import { CopyButton, PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { FONT_MONO } from '../theme';
import { errorMessage } from '../utils/format';

function Section({ title, description, children }) {
  return (
    <Grid container spacing={{ xs: 2, md: 4 }}>
      <Grid size={{ xs: 12, md: 4 }}>
        <Typography variant="subtitle1" component="h2">{title}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{description}</Typography>
      </Grid>
      <Grid size={{ xs: 12, md: 8 }}>
        <Card sx={{ p: 2.5 }}><Stack spacing={2.5}>{children}</Stack></Card>
      </Grid>
    </Grid>
  );
}

export default function SettingsPage() {
  const notify = useNotify();
  const [settings, setSettings] = useState(null);
  const [form, setForm] = useState({ name: '', webhookUrl: '', rotateWebhookSecret: false });
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [showSecret, setShowSecret] = useState(false);

  const apply = (s) => {
    setSettings(s);
    setForm({ name: s.name, webhookUrl: s.webhookUrl ?? '', rotateWebhookSecret: false });
  };

  useEffect(() => {
    api('/tenant').then(apply).catch(setError);
  }, []);

  const save = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      const updated = await api('/tenant', { method: 'PUT', body: { ...form, webhookUrl: form.webhookUrl || null, version: settings.version } });
      apply(updated);
      setError(null);
      notify('Ayarlar kaydedildi');
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  if (!settings) {
    return (
      <>
        <PageHeader title="Şirket ayarları" />
        {error ? <Alert severity="error">{errorMessage(error)}</Alert> : <Skeleton variant="rounded" height={320} />}
      </>
    );
  }

  const dirty = form.name !== settings.name || form.webhookUrl !== (settings.webhookUrl ?? '') || form.rotateWebhookSecret;

  return (
    <Box component="form" onSubmit={save}>
      <PageHeader title="Şirket ayarları" subtitle="Şirket bilgileri ve entegrasyon ayarları" />
      {error && <Alert severity="error" sx={{ mb: 3 }}>{errorMessage(error)}</Alert>}
      <Stack spacing={4} divider={<Divider />}>
        <Section title="Genel" description="Şirketinizin uygulamada ve raporlarda görünen bilgileri.">
          <TextField label="Şirket adı" value={form.name} required onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextField label="Kısa ad" value={settings.slug} disabled helperText="Kısa ad sonradan değiştirilemez." />
        </Section>

        <Section
          title="Webhook"
          description="İşlem olayları (oluşturuldu, onaylandı, …) bu adrese imzalı olarak gönderilir; hata olursa artan aralıklarla yeniden denenir."
        >
          <TextField label="Webhook adresi" type="url" placeholder="https://ornek.com/nexora-webhook" value={form.webhookUrl}
            onChange={(e) => setForm({ ...form, webhookUrl: e.target.value })} helperText="Boş bırakılırsa olay gönderilmez." />
          {settings.webhookSecret && (
            <>
              <TextField
                label="İmza anahtarı"
                value={showSecret ? settings.webhookSecret : '•'.repeat(32)}
                slotProps={{
                  htmlInput: { style: { fontFamily: FONT_MONO, fontSize: 13 } },
                  input: {
                    readOnly: true,
                    endAdornment: (
                      <InputAdornment position="end">
                        <Tooltip title={showSecret ? 'Gizle' : 'Göster'}>
                          <IconButton size="small" aria-label={showSecret ? 'Anahtarı gizle' : 'Anahtarı göster'} onClick={() => setShowSecret((v) => !v)}>
                            {showSecret ? <HideIcon fontSize="small" /> : <ShowIcon fontSize="small" />}
                          </IconButton>
                        </Tooltip>
                        <CopyButton value={settings.webhookSecret} label="Anahtarı kopyala" />
                      </InputAdornment>
                    ),
                  },
                }}
              />
              <Box sx={{ fontFamily: FONT_MONO, fontSize: 12.5, px: 1.5, py: 1, borderRadius: 1.5, bgcolor: 'action.hover', color: 'text.secondary', overflowX: 'auto' }}>
                X-Nexora-Signature = HMAC-SHA256(anahtar, zaman + &quot;.&quot; + gövde)
              </Box>
              <FormControlLabel
                control={<Checkbox checked={form.rotateWebhookSecret} onChange={(e) => setForm({ ...form, rotateWebhookSecret: e.target.checked })} />}
                label="Kaydederken anahtarı yenile (eski anahtar hemen geçersiz olur)"
              />
            </>
          )}
        </Section>
      </Stack>

      <Stack direction="row" spacing={2} sx={{ mt: 4, alignItems: 'center', justifyContent: 'flex-end' }}>
        <Typography variant="caption" color="text.secondary">Sürüm {settings.version}</Typography>
        <Button variant="outlined" color="inherit" disabled={!dirty || busy} onClick={() => apply(settings)}>Değişiklikleri geri al</Button>
        <Button type="submit" variant="contained" loading={busy} disabled={!dirty}>Kaydet</Button>
      </Stack>
    </Box>
  );
}
