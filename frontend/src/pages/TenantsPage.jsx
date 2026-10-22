import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import { api } from '../api/client';
import { PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { dateTime, errorMessage } from '../utils/format';

const EMPTY = { name: '', slug: '', adminEmail: '', adminFullName: '', adminPassword: '' };

export default function TenantsPage() {
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState(EMPTY);
  const [error, setError] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api('/platform/tenants', { query: { pageSize: 100 } }).then((r) => setRows(r.items)).finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const toggle = async (tenant) => {
    try {
      await api(`/platform/tenants/${tenant.id}/status`, {
        method: 'PATCH',
        body: { status: tenant.status === 'Active' ? 'Suspended' : 'Active', version: tenant.version },
      });
      notify(tenant.status === 'Active' ? `${tenant.name} askıya alındı` : `${tenant.name} etkinleştirildi`);
      load();
    } catch (err) {
      notify(errorMessage(err), 'error');
    }
  };

  const create = async () => {
    try {
      await api('/platform/tenants', { method: 'POST', body: form });
      setCreating(false);
      notify('Şirket oluşturuldu');
      load();
    } catch (err) {
      setError(err);
    }
  };

  const columns = [
    { field: 'name', headerName: 'Şirket', flex: 1, minWidth: 180 },
    { field: 'slug', headerName: 'Kısa ad', width: 140 },
    { field: 'userCount', headerName: 'Kullanıcı', width: 100, align: 'right', headerAlign: 'right' },
    { field: 'branchCount', headerName: 'Şube', width: 90, align: 'right', headerAlign: 'right' },
    { field: 'createdAt', headerName: 'Oluşturulma', width: 150, valueFormatter: (v) => dateTime(v) },
    { field: 'status', headerName: 'Durum', width: 120, renderCell: (p) => <Chip size="small" color={p.value === 'Active' ? 'success' : 'error'} variant="outlined" label={p.value === 'Active' ? 'Aktif' : 'Askıda'} /> },
    {
      field: 'actions', headerName: '', width: 140, sortable: false,
      renderCell: (p) => <Button size="small" color={p.row.status === 'Active' ? 'error' : 'success'} onClick={() => toggle(p.row)}>{p.row.status === 'Active' ? 'Askıya al' : 'Etkinleştir'}</Button>,
    },
  ];

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  return (
    <>
      <PageHeader title="Şirketler" subtitle="Askıya alınan şirketin kullanıcıları anında erişimini kaybeder."
        actions={<Button variant="contained" startIcon={<AddIcon />} onClick={() => { setForm(EMPTY); setError(null); setCreating(true); }}>Yeni şirket</Button>} />
      <Card>
        <DataGrid autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick hideFooter sx={{ border: 0 }} />
      </Card>
      <Dialog open={creating} onClose={() => setCreating(false)} fullWidth maxWidth="xs">
        <DialogTitle>Yeni şirket</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {error && <Alert severity="error">{errorMessage(error)}</Alert>}
            <TextField label="Şirket adı" value={form.name} onChange={set('name')} />
            <TextField label="Kısa ad (slug)" value={form.slug} onChange={set('slug')} helperText="Küçük harf, rakam ve tire" />
            <TextField label="Yönetici e-postası" value={form.adminEmail} onChange={set('adminEmail')} />
            <TextField label="Yönetici adı" value={form.adminFullName} onChange={set('adminFullName')} />
            <TextField label="Yönetici parolası" type="password" value={form.adminPassword} onChange={set('adminPassword')} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCreating(false)}>Vazgeç</Button>
          <Button variant="contained" onClick={create}>Oluştur</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
