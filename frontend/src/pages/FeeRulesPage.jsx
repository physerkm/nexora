import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch, TextField,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { CURRENCIES, errorMessage, TYPES } from '../utils/format';

export default function FeeRulesPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api('/fee-rules').then(setRows).finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const manage = can('manageTenant') && canWrite;
  const num = (v) => (v === null || v === undefined ? '—' : Number(v).toLocaleString('tr-TR'));

  const columns = [
    { field: 'transactionType', headerName: 'İşlem türü', width: 150, valueFormatter: (v) => TYPES[v] ?? v },
    { field: 'currency', headerName: 'Para birimi', width: 120, valueFormatter: (v) => v ?? 'Tümü' },
    { field: 'percentage', headerName: 'Oran (%)', width: 110, align: 'right', headerAlign: 'right', valueFormatter: num },
    { field: 'fixedAmount', headerName: 'Sabit', width: 100, align: 'right', headerAlign: 'right', valueFormatter: num },
    { field: 'minFee', headerName: 'Min', width: 100, align: 'right', headerAlign: 'right', valueFormatter: num },
    { field: 'maxFee', headerName: 'Maks', width: 100, align: 'right', headerAlign: 'right', valueFormatter: num },
    { field: 'isActive', headerName: 'Durum', width: 110, renderCell: (p) => <Chip size="small" variant="outlined" color={p.value ? 'success' : 'default'} label={p.value ? 'Aktif' : 'Pasif'} /> },
  ];

  return (
    <>
      <PageHeader title="Ücret kuralları" subtitle="Ücret = tutar × oran + sabit; min/maks ile sınırlandırılır. Para birimine özel kural genel kurala göre önceliklidir."
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni kural</Button>} />
      <Card>
        <DataGrid autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick hideFooter
          onRowClick={(p) => manage && setEditing(p.row)} sx={{ border: 0 }} />
      </Card>
      <FeeRuleDialog rule={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); notify('Kural kaydedildi'); load(); }} />
    </>
  );
}

function FeeRuleDialog({ rule, onClose, onSaved }) {
  const isNew = rule && !rule.id;
  const [form, setForm] = useState({});
  const [error, setError] = useState(null);

  useEffect(() => {
    if (rule) {
      setForm({
        transactionType: rule.transactionType ?? 'Payment', currency: rule.currency ?? '', percentage: rule.percentage ?? 0,
        fixedAmount: rule.fixedAmount ?? 0, minFee: rule.minFee ?? 0, maxFee: rule.maxFee ?? '', isActive: rule.isActive ?? true,
      });
      setError(null);
    }
  }, [rule]);

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  const save = async () => {
    const pricing = {
      percentage: Number(form.percentage), fixedAmount: Number(form.fixedAmount), minFee: Number(form.minFee),
      maxFee: form.maxFee === '' ? null : Number(form.maxFee),
    };
    try {
      if (isNew) {
        await api('/fee-rules', { method: 'POST', body: { transactionType: form.transactionType, currency: form.currency || null, ...pricing } });
      } else {
        await api(`/fee-rules/${rule.id}`, { method: 'PUT', body: { ...pricing, isActive: form.isActive, version: rule.version } });
      }
      onSaved();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <Dialog open={!!rule} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{isNew ? 'Yeni ücret kuralı' : 'Kuralı düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField select label="İşlem türü" value={form.transactionType ?? ''} onChange={set('transactionType')} disabled={!isNew}>
            {Object.entries(TYPES).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
          </TextField>
          <TextField select label="Para birimi" value={form.currency ?? ''} onChange={set('currency')} disabled={!isNew}>
            <MenuItem value="">Tümü</MenuItem>
            {CURRENCIES.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
          </TextField>
          <Stack direction="row" spacing={2}>
            <TextField label="Oran (%)" type="number" value={form.percentage ?? ''} onChange={set('percentage')} />
            <TextField label="Sabit" type="number" value={form.fixedAmount ?? ''} onChange={set('fixedAmount')} />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField label="Min ücret" type="number" value={form.minFee ?? ''} onChange={set('minFee')} />
            <TextField label="Maks ücret" type="number" value={form.maxFee ?? ''} onChange={set('maxFee')} helperText="Boş = sınırsız" />
          </Stack>
          {!isNew && <FormControlLabel control={<Switch checked={!!form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />} label="Aktif" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Vazgeç</Button>
        <Button variant="contained" onClick={save}>Kaydet</Button>
      </DialogActions>
    </Dialog>
  );
}
