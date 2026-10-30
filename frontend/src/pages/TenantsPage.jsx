import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Avatar, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, TextField, Typography,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/AddRounded';
import BusinessIcon from '@mui/icons-material/ApartmentOutlined';
import { api } from '../api/client';
import { ConfirmDialog, EmptyState, PageHeader, Pill } from '../components/Common';
import { useNotify } from '../components/Notify';
import PasswordField from '../components/PasswordField';
import { FONT_MONO } from '../theme';
import { dateTime, errorMessage, initials } from '../utils/format';

const EMPTY = { name: '', slug: '', adminEmail: '', adminFullName: '', adminPassword: '' };

export default function TenantsPage() {
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [creating, setCreating] = useState(false);
  const [confirming, setConfirming] = useState(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    api('/platform/tenants', { query: { pageSize: 100 } })
      .then((r) => {
        setRows(r.items);
        setError(null);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, []);
  useEffect(load, [load]);

  const toggle = async (tenant) => {
    setBusy(true);
    try {
      await api(`/platform/tenants/${tenant.id}/status`, {
        method: 'PATCH',
        body: { status: tenant.status === 'Active' ? 'Suspended' : 'Active', version: tenant.version },
      });
      notify(tenant.status === 'Active' ? `${tenant.name} askıya alındı` : `${tenant.name} etkinleştirildi`);
      setConfirming(null);
      load();
    } catch (err) {
      notify(errorMessage(err), 'error');
    } finally {
      setBusy(false);
    }
  };

  const columns = [
    {
      field: 'name', headerName: 'Şirket', flex: 1, minWidth: 220,
      renderCell: (p) => (
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', height: '100%' }}>
          <Avatar variant="rounded" sx={{ width: 30, height: 30, bgcolor: 'action.selected', color: 'primary.main' }}>{initials(p.value)}</Avatar>
          <Typography variant="body2" noWrap sx={{ fontWeight: 500 }}>{p.value}</Typography>
        </Stack>
      ),
    },
    { field: 'slug', headerName: 'Kısa ad', width: 140, renderCell: (p) => <span style={{ fontFamily: FONT_MONO, fontSize: 13 }}>{p.value}</span> },
    { field: 'userCount', headerName: 'Kullanıcı', width: 100, align: 'right', headerAlign: 'right' },
    { field: 'branchCount', headerName: 'Şube', width: 90, align: 'right', headerAlign: 'right' },
    { field: 'createdAt', headerName: 'Oluşturulma', width: 160, valueFormatter: (v) => dateTime(v) },
    {
      field: 'status', headerName: 'Durum', width: 120,
      renderCell: (p) => <Pill tone={p.value === 'Active' ? 'success' : 'error'}>{p.value === 'Active' ? 'Aktif' : 'Askıda'}</Pill>,
    },
    {
      field: 'actions', headerName: '', width: 140, sortable: false, align: 'right',
      renderCell: (p) => (
        <Button size="small" variant="outlined" color={p.row.status === 'Active' ? 'error' : 'success'} onClick={() => setConfirming(p.row)}>
          {p.row.status === 'Active' ? 'Askıya al' : 'Etkinleştir'}
        </Button>
      ),
    },
  ];

  const suspending = confirming?.status === 'Active';

  return (
    <>
      <PageHeader
        title="Şirketler"
        subtitle="Platformdaki tüm müşteri şirketleri"
        actions={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>Yeni şirket</Button>}
      />
      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}
      <Card>
        <DataGrid
          autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick hideFooter
          slots={{ noRowsOverlay: () => <EmptyState icon={<BusinessIcon />} title="Henüz şirket yok" /> }}
          sx={{ '--DataGrid-overlayHeight': '240px' }}
        />
      </Card>

      <ConfirmDialog
        open={!!confirming}
        busy={busy}
        title={suspending ? 'Şirketi askıya al' : 'Şirketi etkinleştir'}
        description={suspending
          ? `${confirming?.name} kullanıcıları erişimini hemen kaybeder. Daha sonra yeniden etkinleştirebilirsiniz.`
          : `${confirming?.name} kullanıcıları yeniden giriş yapabilecek.`}
        confirmLabel={suspending ? 'Askıya al' : 'Etkinleştir'}
        tone={suspending ? 'error' : 'primary'}
        onClose={() => setConfirming(null)}
        onConfirm={() => toggle(confirming)}
      />
      <NewTenantDialog open={creating} onClose={() => setCreating(false)} onCreated={() => { setCreating(false); notify('Şirket oluşturuldu'); load(); }} />
    </>
  );
}

function NewTenantDialog({ open, onClose, onCreated }) {
  const [form, setForm] = useState(EMPTY);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setForm(EMPTY);
      setError(null);
    }
  }, [open]);

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  const create = async (e) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api('/platform/tenants', { method: 'POST', body: form });
      onCreated();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs" slotProps={{ paper: { component: 'form', onSubmit: create } }}>
      <DialogTitle>Yeni şirket</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="Şirket adı" value={form.name} onChange={set('name')} required autoFocus />
          <TextField label="Kısa ad" value={form.slug} onChange={set('slug')} required helperText="Küçük harf, rakam ve tire (ör. acme-finans)" />
          <Divider textAlign="left" sx={{ typography: 'caption', color: 'text.secondary', pt: 1 }}>İlk yönetici hesabı</Divider>
          <TextField label="Yönetici e-postası" type="email" value={form.adminEmail} onChange={set('adminEmail')} required />
          <TextField label="Yönetici adı" value={form.adminFullName} onChange={set('adminFullName')} required />
          <PasswordField label="Yönetici parolası" value={form.adminPassword} onChange={set('adminPassword')} required autoComplete="new-password" />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button type="submit" variant="contained" loading={busy}>Oluştur</Button>
      </DialogActions>
    </Dialog>
  );
}
