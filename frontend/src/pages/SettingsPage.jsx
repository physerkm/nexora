import { useEffect, useState } from 'react';
import { Alert, Button, Card, CardContent, Checkbox, FormControlLabel, LinearProgress, Stack, TextField, Typography } from '@mui/material';
import { api } from '../api/client';
import { PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { errorMessage } from '../utils/format';

export default function SettingsPage() {
  const notify = useNotify();
  const [settings, setSettings] = useState(null);
  const [form, setForm] = useState({ name: '', webhookUrl: '', rotateWebhookSecret: false });
  const [error, setError] = useState(null);

  const apply = (s) => {
    setSettings(s);
    setForm({ name: s.name, webhookUrl: s.webhookUrl ?? '', rotateWebhookSecret: false });
  };

  useEffect(() => {
    api('/tenant').then(apply).catch(setError);
  }, []);

  const save = async () => {
    try {
      const updated = await api('/tenant', { method: 'PUT', body: { ...form, webhookUrl: form.webhookUrl || null, version: settings.version } });
      apply(updated);
      setError(null);
      notify('Ayarlar kaydedildi');
    } catch (err) {
      setError(err);
    }
  };

  if (!settings) return error ? <Alert severity="error">{errorMessage(error)}</Alert> : <LinearProgress />;

  return (
    <>
      <PageHeader title="Şirket ayarları" subtitle={settings.slug} />
      <Card sx={{ maxWidth: 640 }}>
        <CardContent>
          <Stack spacing={2.5}>
            {error && <Alert severity="error">{errorMessage(error)}</Alert>}
            <TextField label="Şirket adı" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            <TextField label="Webhook URL" placeholder="https://ornek.com/nexora-webhook" value={form.webhookUrl}
              onChange={(e) => setForm({ ...form, webhookUrl: e.target.value })}
              helperText="İşlem olayları (oluşturuldu, onaylandı, ...) bu adrese imzalı olarak gönderilir; hata olursa artan aralıklarla yeniden denenir." />
            {settings.webhookSecret && (
              <>
                <TextField label="İmza anahtarı" value={settings.webhookSecret} slotProps={{ input: { readOnly: true } }}
                  helperText="X-Nexora-Signature = HMAC-SHA256(anahtar, zaman + '.' + gövde)" />
                <FormControlLabel control={<Checkbox checked={form.rotateWebhookSecret} onChange={(e) => setForm({ ...form, rotateWebhookSecret: e.target.checked })} />}
                  label="Kaydederken anahtarı yenile" />
              </>
            )}
            <Typography variant="caption" color="text.secondary">Sürüm: {settings.version}</Typography>
            <Stack direction="row"><Button variant="contained" onClick={save}>Kaydet</Button></Stack>
          </Stack>
        </CardContent>
      </Card>
    </>
  );
}
