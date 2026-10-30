import { useEffect, useRef, useState } from 'react';
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Divider, MenuItem, Stack, TextField, Typography,
} from '@mui/material';
import { api, newIdempotencyKey } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useDebounced } from '../components/Common';
import { CURRENCIES, errorMessage, money, TYPES } from '../utils/format';

const EMPTY = { branchId: '', type: 'Payment', amount: '', currency: 'TRY', counterpartyName: '', description: '' };

function QuoteRow({ label, value, strong }) {
  return (
    <Stack direction="row" sx={{ justifyContent: 'space-between', py: 0.5 }}>
      <Typography variant="body2" color={strong ? 'text.primary' : 'text.secondary'} sx={{ fontWeight: strong ? 600 : 400 }}>{label}</Typography>
      <Typography variant="body2" sx={{ fontWeight: strong ? 600 : 500, fontVariantNumeric: 'tabular-nums' }}>{value}</Typography>
    </Stack>
  );
}

export default function NewTransactionDialog({ open, branches, onClose, onCreated }) {
  const { user } = useAuth();
  const [form, setForm] = useState(EMPTY);
  const [quote, setQuote] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  // One key per logical submission: a double click or a retry after a network error is not
  // executed twice by the API, it replays the first result.
  const idempotencyKey = useRef(null);

  useEffect(() => {
    if (open) {
      setForm({ ...EMPTY, branchId: user.branchId ?? branches.find((b) => b.isActive)?.id ?? '' });
      setError(null);
      setQuote(null);
      idempotencyKey.current = newIdempotencyKey();
    }
  }, [open, user.branchId, branches]);

  const amount = useDebounced(form.amount, 350);
  useEffect(() => {
    const value = Number(amount);
    if (!open || !value || value <= 0) {
      setQuote(null);
      return;
    }
    api('/fees/quote', { method: 'POST', body: { type: form.type, amount: value, currency: form.currency } })
      .then(setQuote)
      .catch(() => setQuote(null));
  }, [open, amount, form.type, form.currency]);

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

  const submit = async (e) => {
    e?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const tx = await api('/transactions', {
        method: 'POST',
        idempotencyKey: idempotencyKey.current,
        body: { ...form, amount: Number(form.amount), counterpartyName: form.counterpartyName || null, description: form.description || null },
      });
      onCreated(tx);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const valid = !!form.branchId && Number(form.amount) > 0;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" slotProps={{ paper: { component: 'form', onSubmit: submit, noValidate: true } }}>
      <DialogTitle>Yeni işlem</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
          İşlem onaya gönderilir; yetkili bir yönetici onaylayana kadar beklemede kalır.
        </Typography>
        <Stack spacing={2.5}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField select label="Şube" value={form.branchId} onChange={set('branchId')} disabled={!!user.branchId}
            helperText={user.branchId ? 'Hesabınız bu şubeye bağlı' : undefined}>
            {branches.filter((b) => b.isActive).map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
          </TextField>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField select label="Tür" value={form.type} onChange={set('type')} sx={{ flex: 1 }}>
              {Object.entries(TYPES).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
            </TextField>
            <TextField label="Tutar" type="number" value={form.amount} onChange={set('amount')} sx={{ flex: 1 }}
              slotProps={{ htmlInput: { min: 0.01, step: 0.01, inputMode: 'decimal' } }} />
            <TextField select label="Para birimi" value={form.currency} onChange={set('currency')} sx={{ width: { sm: 130 } }}>
              {CURRENCIES.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
            </TextField>
          </Stack>
          <TextField label="Karşı taraf" value={form.counterpartyName} onChange={set('counterpartyName')} slotProps={{ htmlInput: { maxLength: 200 } }} />
          <TextField label="Açıklama" value={form.description} onChange={set('description')} multiline minRows={2}
            helperText="İsteğe bağlı" slotProps={{ htmlInput: { maxLength: 500 } }} />
          {quote && (
            <Box sx={{ px: 2, py: 1.25, borderRadius: 2, border: 1, borderColor: 'divider', bgcolor: 'background.default' }} aria-live="polite">
              <QuoteRow label="Tutar" value={money(quote.amount, quote.currency)} />
              <QuoteRow label="İşlem ücreti" value={money(quote.fee, quote.currency)} />
              <Divider sx={{ my: 0.75 }} />
              <QuoteRow label="Net tutar" value={money(quote.netAmount, quote.currency)} strong />
              {!quote.feeRuleId && (
                <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 0.5 }}>
                  Bu işlem türü için tanımlı ücret kuralı yok.
                </Typography>
              )}
            </Box>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button type="submit" variant="contained" loading={busy} disabled={!valid}>Oluştur</Button>
      </DialogActions>
    </Dialog>
  );
}
