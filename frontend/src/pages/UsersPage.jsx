import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Avatar, Box, Button, Card, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Stack,
  Switch, TextField, Tooltip, Typography,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/PersonAddAlt1Outlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import PeopleIcon from '@mui/icons-material/PeopleAltOutlined';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { EmptyState, PageHeader, Pill } from '../components/Common';
import { useNotify } from '../components/Notify';
import PasswordField from '../components/PasswordField';
import { dateTime, errorMessage, initials, ROLES } from '../utils/format';

const TENANT_ROLES = ['TenantAdmin', 'Manager', 'Operator', 'Auditor'];
const ROLE_HINTS = {
  TenantAdmin: 'Şirket ayarları, kullanıcılar ve tüm işlemler',
  Manager: 'İşlemleri onaylar ve reddeder, raporları görür',
  Operator: 'İşlem oluşturur ve kendi işlemini iptal eder',
  Auditor: 'Salt okunur: raporlar ve denetim kayıtları',
};

export default function UsersPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [rows, setRows] = useState([]);
  const [branches, setBranches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null);

  const load = useCallback(() => {
    setLoading(true);
    api('/users', { query: { pageSize: 100 } })
      .then((r) => {
        setRows(r.items);
        setError(null);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
    api('/branches', { query: { pageSize: 100 } }).then((r) => setBranches(r.items)).catch(() => {});
  }, [load]);

  const manage = can('manageTenant') && canWrite;
  const branchName = (id) => branches.find((b) => b.id === id)?.name ?? '—';

  const columns = [
    {
      field: 'fullName', headerName: 'Kullanıcı', flex: 1.4, minWidth: 240,
      renderCell: (p) => (
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', height: '100%', minWidth: 0 }}>
          <Avatar sx={{ width: 32, height: 32, bgcolor: 'action.selected', color: 'primary.main' }}>{initials(p.value)}</Avatar>
          <Box sx={{ minWidth: 0, lineHeight: 1.3 }}>
            <Typography variant="body2" noWrap sx={{ fontWeight: 500 }}>{p.value}</Typography>
            <Typography variant="caption" color="text.secondary" noWrap component="p">{p.row.email}</Typography>
          </Box>
        </Stack>
      ),
    },
    { field: 'role', headerName: 'Rol', width: 160, valueFormatter: (v) => ROLES[v] ?? v },
    { field: 'branchId', headerName: 'Şube', width: 170, valueFormatter: (v) => (v ? branchName(v) : 'Tüm şubeler') },
    {
      field: 'isActive', headerName: 'Durum', width: 120,
      renderCell: (p) => {
        if (p.row.isLockedOut) return <Pill tone="warning">Kilitli</Pill>;
        return <Pill tone={p.value ? 'success' : 'neutral'}>{p.value ? 'Aktif' : 'Pasif'}</Pill>;
      },
    },
    { field: 'lastLoginAt', headerName: 'Son giriş', width: 160, valueFormatter: (v) => (v ? dateTime(v) : 'Hiç giriş yapmadı') },
    ...(manage ? [{
      field: 'actions', headerName: '', width: 64, sortable: false, align: 'right',
      renderCell: (p) => (
        <Tooltip title="Düzenle">
          <IconButton size="small" aria-label={`${p.row.fullName} kullanıcısını düzenle`} onClick={(e) => { e.stopPropagation(); setEditing(p.row); }}>
            <EditIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      ),
    }] : []),
  ];

  return (
    <>
      <PageHeader
        title="Kullanıcılar"
        subtitle="Rol değişikliği veya pasife alma, kullanıcının açık oturumlarını hemen sonlandırır."
        actions={manage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setEditing({})}>Yeni kullanıcı</Button>}
      />
      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}
      <Card>
        <DataGrid
          autoHeight rows={rows} columns={columns} loading={loading} disableRowSelectionOnClick rowHeight={60}
          onRowClick={(p) => manage && setEditing(p.row)}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }} pageSizeOptions={[25, 50, 100]}
          slots={{ noRowsOverlay: () => <EmptyState icon={<PeopleIcon />} title="Kullanıcı yok" /> }}
          sx={{ '--DataGrid-overlayHeight': '240px', ...(manage && { '& .MuiDataGrid-row': { cursor: 'pointer' } }) }}
        />
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
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (user) {
      setForm({ email: user.email ?? '', fullName: user.fullName ?? '', password: '', role: user.role ?? 'Operator', branchId: user.branchId ?? '', isActive: user.isActive ?? true });
      setError(null);
    }
  }, [user]);

  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value });

  const save = async (e) => {
    e.preventDefault();
    setBusy(true);
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
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!user} onClose={onClose} fullWidth maxWidth="xs" slotProps={{ paper: { component: 'form', onSubmit: save } }}>
      <DialogTitle>{isNew ? 'Yeni kullanıcı' : 'Kullanıcıyı düzenle'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          {error && <Alert severity="error">{errorMessage(error)}</Alert>}
          <TextField label="E-posta" type="email" value={form.email ?? ''} onChange={set('email')} disabled={!isNew} required={isNew} autoFocus={isNew} />
          <TextField label="Ad soyad" value={form.fullName ?? ''} onChange={set('fullName')} required />
          {isNew && (
            <PasswordField label="Geçici parola" value={form.password ?? ''} onChange={set('password')} required autoComplete="new-password"
              helperText="En az 10 karakter; büyük/küçük harf, rakam ve sembol" />
          )}
          <TextField select label="Rol" value={form.role ?? ''} onChange={set('role')} helperText={ROLE_HINTS[form.role]}>
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
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button type="submit" variant="contained" loading={busy}>Kaydet</Button>
      </DialogActions>
    </Dialog>
  );
}
