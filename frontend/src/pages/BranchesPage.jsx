import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Stack, Switch, TextField, Tooltip,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/AddRounded';
import EditIcon from '@mui/icons-material/EditOutlined';
import StoreIcon from '@mui/icons-material/StorefrontOutlined';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ActivePill, EmptyState, PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { FONT_MONO } from '../theme';
import { dateTime, errorMessage } from '../utils/format';

export default function BranchesPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // null | {} (new) | branch

  const load = useCallback(() => {
    setLoading(true);
    api('/branches', { query: { pageSize: 100 } })
      .then((r) => {
        setRows(r.items);
        setError(null);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const manage = can('manageTenant') && canWrite;

  const columns = [
    { field: 'code', headerName: 'Kod', width: 130, renderCell: (p) => <span style={{ fontFamily: FONT_MONO, fontSize: 13 }}>{p.value}</span> },
    { field: 'name', headerName: 'Ad', flex: 1, minWidth: 180, cellClassName: 'strong' },
    { field: 'city', headerName: 'Şehir', width: 150, valueFormatter: (v) => v || '—' },
    { field: 'isActive', headerName: 'Durum', width: 120, renderCell: (p) => <ActivePill active={p.value} /> },
    { field: 'createdAt', headerName: 'Oluşturulma', width: 160, valueFormatter: (v) => dateTime(v) },
    ...(manage ? [{
      field: 'actions', headerName: '', width: 64, sortable: false, align: 'right',
      renderCell: (p) => (
        <Tooltip title="Düzenle">
          <IconButton size="small" aria-label={`${p.row.name} şubesini düzenle`} onClick={(e) => { e.stopPropagation(); setEditing(p.row); }}>
            <EditIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      ),
    }] : []),
  ];

  return (
    <>
      <PageHeader
        title="Şubeler"
        subtitle="Şirketinize ait şubeler ve durumları"
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni şube</Button>}
      />
      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}
      <Card>
        <DataGrid
          autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick
          onRowClick={(p) => manage && setEditing(p.row)}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }} pageSizeOptions={[25, 50, 100]}
          slots={{ noRowsOverlay: () => <EmptyState icon={<StoreIcon />} title="Henüz şube yok" description="İşlem oluşturabilmek için en az bir şube ekleyin." /> }}
          sx={{ '--DataGrid-overlayHeight': '260px', '& .strong': { fontWeight: 500 }, ...(manage && { '& .MuiDataGrid-row': { cursor: 'pointer' } }) }}
        />
      </Card>
      <BranchDialog branch={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); notify('Şube kaydedildi'); load(); }} />
    </>
  );
}

function BranchDialog({ branch, onClose, onSaved }) {
  const isNew = branch && !branch.id;
  const [form, setForm] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (branch) {
      setForm({ code: branch.code ?? '', name: branch.name ?? '', city: branch.city ?? '', isActive: branch.isActive ?? true });
      setError(null);
    }
  }, [branch]);

  const save = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      if (isNew) {
        await api('/branches', { method: 'POST', body: { code: form.code, name: form.name, city: form.city || null } });
      } else {
        await api(`/branches/${branch.id}`, { method: 'PUT', body: { name: form.name, city: form.city || null, isActive: form.isActive, version: branch.version } });
      }
      onSaved();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!branch} onClose={onClose} fullWidth maxWidth="xs" slotProps={{ paper: { component: 'form', onSubmit: save } }}>
      <DialogTitle>{isNew ? 'Yeni şube' : 'Şubeyi düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="Kod" value={form.code ?? ''} disabled={!isNew} required={isNew} autoFocus={isNew}
            helperText={isNew ? 'Benzersiz kısa kod; sonradan değiştirilemez.' : undefined}
            onChange={(e) => setForm({ ...form, code: e.target.value })} />
          <TextField label="Ad" value={form.name ?? ''} required onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextField label="Şehir" value={form.city ?? ''} onChange={(e) => setForm({ ...form, city: e.target.value })} />
          {!isNew && (
            <FormControlLabel
              control={<Switch checked={!!form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />}
              label="Aktif (pasif şubede yeni işlem oluşturulamaz)"
            />
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button type="submit" variant="contained" loading={busy}>Kaydet</Button>
      </DialogActions>
    </Dialog>
  );
}
