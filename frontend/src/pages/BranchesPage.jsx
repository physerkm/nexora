import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Stack, Switch, TextField,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { dateTime, errorMessage } from '../utils/format';

export default function BranchesPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(null); // null | {} (new) | branch

  const load = useCallback(() => {
    setLoading(true);
    api('/branches', { query: { pageSize: 100 } }).then((r) => setRows(r.items)).finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const manage = can('manageTenant') && canWrite;

  const columns = [
    { field: 'code', headerName: 'Kod', width: 120 },
    { field: 'name', headerName: 'Ad', flex: 1, minWidth: 180 },
    { field: 'city', headerName: 'Şehir', width: 140 },
    { field: 'isActive', headerName: 'Durum', width: 110, renderCell: (p) => <Chip size="small" label={p.value ? 'Aktif' : 'Pasif'} color={p.value ? 'success' : 'default'} variant="outlined" /> },
    { field: 'createdAt', headerName: 'Oluşturulma', width: 150, valueFormatter: (v) => dateTime(v) },
  ];

  return (
    <>
      <PageHeader title="Şubeler" subtitle="Şirketinize ait şubeler"
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni şube</Button>} />
      <Card>
        <DataGrid autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick
          onRowClick={(p) => manage && setEditing(p.row)} sx={{ border: 0 }}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }} pageSizeOptions={[25, 50, 100]} />
      </Card>
      <BranchDialog branch={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); notify('Şube kaydedildi'); load(); }} />
    </>
  );
}

function BranchDialog({ branch, onClose, onSaved }) {
  const isNew = branch && !branch.id;
  const [form, setForm] = useState({});
  const [error, setError] = useState(null);

  useEffect(() => {
    if (branch) {
      setForm({ code: branch.code ?? '', name: branch.name ?? '', city: branch.city ?? '', isActive: branch.isActive ?? true });
      setError(null);
    }
  }, [branch]);

  const save = async () => {
    try {
      if (isNew) {
        await api('/branches', { method: 'POST', body: { code: form.code, name: form.name, city: form.city || null } });
      } else {
        await api(`/branches/${branch.id}`, { method: 'PUT', body: { name: form.name, city: form.city || null, isActive: form.isActive, version: branch.version } });
      }
      onSaved();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <Dialog open={!!branch} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{isNew ? 'Yeni şube' : 'Şubeyi düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="Kod" value={form.code ?? ''} disabled={!isNew} onChange={(e) => setForm({ ...form, code: e.target.value })} />
          <TextField label="Ad" value={form.name ?? ''} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <TextField label="Şehir" value={form.city ?? ''} onChange={(e) => setForm({ ...form, city: e.target.value })} />
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
