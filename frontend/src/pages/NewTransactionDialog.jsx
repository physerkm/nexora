import { useEffect, useRef, useState } from 'react';
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography,
} from '@mui/material';
import { api, newIdempotencyKey } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useDebounced } from '../components/Common';
import { CURRENCIES, errorMessage, money, TYPES } from '../utils/format';

const EMPTY = { branchId: '', type: 'Payment', amount: '', currency: 'TRY', counterpartyName: '', description: '' };

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

  const submit = async () => {
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

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Yeni işlem</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
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
              slotProps={{ htmlInput: { min: 0.01, step: 0.01 } }} />
            <TextField select label="Para birimi" value={form.currency} onChange={set('currency')} sx={{ width: { sm: 120 } }}>
              {CURRENCIES.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
            </TextField>
          </Stack>
          <TextField label="Karşı taraf" value={form.counterpartyName} onChange={set('counterpartyName')} slotProps={{ htmlInput: { maxLength: 200 } }} />
          <TextField label="Açıklama" value={form.description} onChange={set('description')} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 500 } }} />
          {quote && (
            <Typography variant="body2" color="text.secondary">
              Ücret: <b>{money(quote.fee, quote.currency)}</b> · Net: <b>{money(quote.netAmount, quote.currency)}</b>
              {!quote.feeRuleId && ' (bu tür için ücret kuralı yok)'}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Vazgeç</Button>
        <Button variant="contained" onClick={submit} disabled={busy || !form.branchId || !(Number(form.amount) > 0)}>Oluştur</Button>
      </DialogActions>
    </Dialog>
  );
}
