import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch, TextField,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/PersonAddAlt';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { PageHeader } from '../components/Common';
import { useNotify } from '../components/Notify';
import { dateTime, errorMessage, ROLES } from '../utils/format';

const TENANT_ROLES = ['TenantAdmin', 'Manager', 'Operator', 'Auditor'];

export default function UsersPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [branches, setBranches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api('/users', { query: { pageSize: 100 } }).then((r) => setRows(r.items)).finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
    api('/branches', { query: { pageSize: 100 } }).then((r) => setBranches(r.items)).catch(() => {});
  }, [load]);

  const manage = can('manageTenant') && canWrite;
  const branchName = (id) => branches.find((b) => b.id === id)?.name ?? '—';

  const columns = [
    { field: 'fullName', headerName: 'Ad soyad', flex: 1, minWidth: 160 },
    { field: 'email', headerName: 'E-posta', flex: 1.2, minWidth: 200 },
    { field: 'role', headerName: 'Rol', width: 150, valueFormatter: (v) => ROLES[v] ?? v },
    { field: 'branchId', headerName: 'Şube', width: 160, valueFormatter: (v) => (v ? branchName(v) : 'Tüm şubeler') },
    {
      field: 'isActive', headerName: 'Durum', width: 120,
      renderCell: (p) => (p.row.isLockedOut ? <Chip size="small" color="warning" label="Kilitli" /> :
        <Chip size="small" variant="outlined" color={p.value ? 'success' : 'default'} label={p.value ? 'Aktif' : 'Pasif'} />),
    },
    { field: 'lastLoginAt', headerName: 'Son giriş', width: 150, valueFormatter: (v) => dateTime(v) },
  ];

  return (
    <>
      <PageHeader title="Kullanıcılar" subtitle="Rol değişikliği veya pasife alma, kullanıcının açık oturumlarını hemen sonlandırır."
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni kullanıcı</Button>} />
      <Card>
        <DataGrid autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick
          onRowClick={(p) => manage && setEditing(p.row)} sx={{ border: 0 }}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }} pageSizeOptions={[25, 50, 100]} />
      </Card>
      <UserDialog user={editing} branches={branches} onClose={() => setEditing(null)}
        onSaved={() => { setEditing(null); notify('Kullanıcı kaydedildi'); load(); }} />
    </>
  );
}

function UserDialog({ user, branches, onClose, onSaved }) {
  const isNew = user && !user.id;
  const [form, setForm] = useState({});
  const [error, setError] = useState(null);

  useEffect(() => {
    if (user) {
      setForm({ email: user.email ?? '', fullName: user.fullName ?? '', password: '', role: user.role ?? 'Operator', branchId: user.branchId ?? '', isActive: user.isActive ?? true });
      setError(null);
    }
  }, [user]);

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  const save = async () => {
    try {
      const branchId = form.branchId || null;
      if (isNew) {
        await api('/users', { method: 'POST', body: { email: form.email, fullName: form.fullName, password: form.password, role: form.role, branchId } });
      } else {
        await api(`/users/${user.id}`, { method: 'PUT', body: { fullName: form.fullName, role: form.role, branchId, isActive: form.isActive } });
      }
      onSaved();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <Dialog open={!!user} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{isNew ? 'Yeni kullanıcı' : 'Kullanıcıyı düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="E-posta" value={form.email ?? ''} onChange={set('email')} disabled={!isNew} />
          <TextField label="Ad soyad" value={form.fullName ?? ''} onChange={set('fullName')} />
          {isNew && <TextField label="Geçici parola" type="password" value={form.password ?? ''} onChange={set('password')}
            helperText="En az 10 karakter; büyük/küçük harf, rakam ve sembol" />}
          <TextField select label="Rol" value={form.role ?? ''} onChange={set('role')}>
            {TENANT_ROLES.map((r) => <MenuItem key={r} value={r}>{ROLES[r]}</MenuItem>)}
          </TextField>
          <TextField select label="Şube kısıtı" value={form.branchId ?? ''} onChange={set('branchId')}
            helperText="Seçilirse kullanıcı yalnızca bu şubenin işlemlerinde işlem yapabilir">
            <MenuItem value="">Tüm şubeler</MenuItem>
            {branches.map((b) => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
          </TextField>
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
