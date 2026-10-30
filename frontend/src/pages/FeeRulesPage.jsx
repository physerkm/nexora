import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, InputAdornment, MenuItem,
  Stack, Switch, TextField, Tooltip, Typography,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/AddRounded';
import EditIcon from '@mui/icons-material/EditOutlined';
import PercentIcon from '@mui/icons-material/PercentOutlined';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ActivePill, EmptyState, PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { FONT_MONO } from '../theme';
import { CURRENCIES, errorMessage, TYPES } from '../utils/format';

const num = (v) => (v === null || v === undefined ? '—' : Number(v).toLocaleString('tr-TR'));

export default function FeeRulesPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api('/fee-rules')
      .then((r) => {
        setRows(r);
        setError(null);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const manage = can('manageTenant') && canWrite;
  const numeric = { align: 'right', headerAlign: 'right', cellClassName: 'num' };

  const columns = [
    { field: 'transactionType', headerName: 'İşlem türü', flex: 1, minWidth: 140, cellClassName: 'strong', valueFormatter: (v) => TYPES[v] ?? v },
    { field: 'currency', headerName: 'Para birimi', width: 120, valueFormatter: (v) => v ?? 'Tümü' },
    { field: 'percentage', headerName: 'Oran', width: 100, ...numeric, valueFormatter: (v) => (v == null ? '—' : `%${num(v)}`) },
    { field: 'fixedAmount', headerName: 'Sabit', width: 100, ...numeric, valueFormatter: num },
    { field: 'minFee', headerName: 'En az', width: 100, ...numeric, valueFormatter: num },
    { field: 'maxFee', headerName: 'En çok', width: 100, ...numeric, valueFormatter: (v) => (v == null ? 'Sınırsız' : num(v)) },
    { field: 'isActive', headerName: 'Durum', width: 120, renderCell: (p) => <ActivePill active={p.value} /> },
    ...(manage ? [{
      field: 'actions', headerName: '', width: 64, sortable: false, align: 'right',
      renderCell: (p) => (
        <Tooltip title="Düzenle">
          <IconButton size="small" aria-label={`${TYPES[p.row.transactionType]} kuralını düzenle`} onClick={(e) => { e.stopPropagation(); setEditing(p.row); }}>
            <EditIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      ),
    }] : []),
  ];

  return (
    <>
      <PageHeader
        title="Ücret kuralları"
        subtitle="İşlem türüne ve para birimine göre uygulanan ücretler"
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni kural</Button>}
      />
      <Card sx={{ mb: 2.5, px: 2.5, py: 2 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={{ xs: 1, md: 3 }} sx={{ alignItems: { md: 'center' } }}>
          <Box sx={{ fontFamily: FONT_MONO, fontSize: 13, px: 1.5, py: 1, borderRadius: 1.5, bgcolor: 'action.hover', whiteSpace: 'nowrap' }}>
            ücret = tutar × oran + sabit
          </Box>
          <Typography variant="body2" color="text.secondary">
            Sonuç en az / en çok sınırları arasında tutulur. Para birimine özel bir kural varsa, &quot;Tümü&quot; kuralına göre önceliklidir.
          </Typography>
        </Stack>
      </Card>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}
      <Card>
        <DataGrid
          autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick hideFooter
          onRowClick={(p) => manage && setEditing(p.row)}
          slots={{ noRowsOverlay: () => <EmptyState icon={<PercentIcon />} title="Ücret kuralı yok" description="Kural tanımlanmayan işlemlerden ücret alınmaz." /> }}
          sx={{
            '--DataGrid-overlayHeight': '260px',
            '& .strong': { fontWeight: 500 },
            '& .num': { fontVariantNumeric: 'tabular-nums' },
            ...(manage && { '& .MuiDataGrid-row': { cursor: 'pointer' } }),
          }}
        />
      </Card>
      <FeeRuleDialog rule={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); notify('Kural kaydedildi'); load(); }} />
    </>
  );
}

function FeeRuleDialog({ rule, onClose, onSaved }) {
  const isNew = rule && !rule.id;
  const [form, setForm] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

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

  const save = async (e) => {
    e.preventDefault();
    const pricing = {
      percentage: Number(form.percentage), fixedAmount: Number(form.fixedAmount), minFee: Number(form.minFee),
      maxFee: form.maxFee === '' ? null : Number(form.maxFee),
    };
    setBusy(true);
    try {
      if (isNew) {
        await api('/fee-rules', { method: 'POST', body: { transactionType: form.transactionType, currency: form.currency || null, ...pricing } });
      } else {
        await api(`/fee-rules/${rule.id}`, { method: 'PUT', body: { ...pricing, isActive: form.isActive, version: rule.version } });
      }
      onSaved();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const numberInput = { htmlInput: { min: 0, step: 0.01, inputMode: 'decimal' } };

  return (
    <Dialog open={!!rule} onClose={onClose} fullWidth maxWidth="xs" slotProps={{ paper: { component: 'form', onSubmit: save } }}>
      <DialogTitle>{isNew ? 'Yeni ücret kuralı' : 'Kuralı düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <Stack direction="row" spacing={2}>
            <TextField select fullWidth label="İşlem türü" value={form.transactionType ?? ''} onChange={set('transactionType')} disabled={!isNew}>
              {Object.entries(TYPES).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
            </TextField>
            <TextField select fullWidth label="Para birimi" value={form.currency ?? ''} onChange={set('currency')} disabled={!isNew}>
              <MenuItem value="">Tümü</MenuItem>
              {CURRENCIES.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
            </TextField>
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField label="Oran" type="number" value={form.percentage ?? ''} onChange={set('percentage')}
              slotProps={{ ...numberInput, input: { startAdornment: <InputAdornment position="start">%</InputAdornment> } }} />
            <TextField label="Sabit" type="number" value={form.fixedAmount ?? ''} onChange={set('fixedAmount')} slotProps={numberInput} />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField label="En az ücret" type="number" value={form.minFee ?? ''} onChange={set('minFee')} slotProps={numberInput} />
            <TextField label="En çok ücret" type="number" value={form.maxFee ?? ''} onChange={set('maxFee')} helperText="Boş bırakılırsa sınırsız" slotProps={numberInput} />
          </Stack>
          {!isNew && <FormControlLabel control={<Switch checked={!!form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />} label="Aktif" />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button type="submit" variant="contained" loading={busy}>Kaydet</Button>
      </DialogActions>
    </Dialog>
  );
}
