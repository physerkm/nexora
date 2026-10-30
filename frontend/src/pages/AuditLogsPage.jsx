import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert, Box, Card, Dialog, DialogContent, DialogTitle, Divider, IconButton, InputAdornment, MenuItem, Stack, TextField, Typography,
} from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import CloseIcon from '@mui/icons-material/CloseRounded';
import SearchIcon from '@mui/icons-material/SearchRounded';
import HistoryIcon from '@mui/icons-material/ManageSearchOutlined';
import LockIcon from '@mui/icons-material/LockOutlined';
import { api } from '../api/client';
import { CopyButton, DetailList, EmptyState, PageHeader, useDebounced } from '../components/Common';
import { FONT_MONO } from '../theme';
import { dateTime, ENTITY_TYPES, errorMessage } from '../utils/format';

const mono = { fontFamily: FONT_MONO, fontSize: 12.5 };
// The API records background jobs as "system".
const actor = (email) => (!email || email === 'system' ? 'Sistem' : email);

const columns = [
  { field: 'createdAt', headerName: 'Zaman', width: 160, valueFormatter: (v) => dateTime(v) },
  {
    field: 'action', headerName: 'Eylem', flex: 1, minWidth: 220,
    renderCell: (p) => (
      <Box component="span" sx={{ ...mono, px: 0.75, py: 0.25, borderRadius: 1, bgcolor: 'action.hover', border: 1, borderColor: 'divider' }}>
        {p.value}
      </Box>
    ),
  },
  { field: 'entityType', headerName: 'Kayıt türü', width: 130, valueFormatter: (v) => ENTITY_TYPES[v] ?? v },
  { field: 'userEmail', headerName: 'Kullanıcı', flex: 1, minWidth: 190, valueFormatter: actor },
  { field: 'ipAddress', headerName: 'IP', width: 140, renderCell: (p) => <Box component="span" sx={mono}>{p.value ?? '—'}</Box> },
];

export default function AuditLogsPage() {
  const [pagination, setPagination] = useState({ page: 0, pageSize: 25 });
  const [entityType, setEntityType] = useState('');
  const [action, setAction] = useState('');
  const [data, setData] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [selected, setSelected] = useState(null);
  const debouncedAction = useDebounced(action);
  const latest = useRef(0);

  const query = useMemo(() => ({ page: pagination.page + 1, pageSize: pagination.pageSize, entityType, action: debouncedAction }),
    [pagination, entityType, debouncedAction]);

  useEffect(() => {
    const request = ++latest.current;
    setLoading(true);
    api('/audit-logs', { query })
      .then((r) => {
        if (request !== latest.current) return;
        setData(r);
        setError(null);
      })
      .catch((err) => request === latest.current && setError(err))
      .finally(() => request === latest.current && setLoading(false));
  }, [query]);

  const resetPage = () => setPagination((p) => ({ ...p, page: 0 }));

  return (
    <>
      <PageHeader title="Denetim kayıtları" subtitle="Sistemde yapılan her değişikliğin kaydı" />
      <Alert severity="info" icon={<LockIcon fontSize="inherit" />} sx={{ mb: 2.5 }}>
        Bu kayıtlar değiştirilemez: veritabanı güncelleme ve silme işlemlerini reddeder.
      </Alert>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}
      <Card>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.25} sx={{ p: 2 }}>
          <TextField select size="small" label="Kayıt türü" value={entityType} sx={{ minWidth: 180 }}
            onChange={(e) => { setEntityType(e.target.value); resetPage(); }}>
            <MenuItem value="">Tümü</MenuItem>
            {Object.entries(ENTITY_TYPES).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
          </TextField>
          <TextField size="small" label="Eylem" placeholder="ör. transaction." value={action} sx={{ minWidth: 260 }}
            onChange={(e) => { setAction(e.target.value); resetPage(); }}
            slotProps={{
              inputLabel: { shrink: true },
              input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> },
            }}
            helperText="Başlangıcına göre arar" />
        </Stack>
        <Divider />
        <DataGrid
          autoHeight rows={data.items} columns={columns} rowCount={data.totalCount ?? 0} loading={loading}
          paginationMode="server" paginationModel={pagination} onPaginationModelChange={setPagination}
          pageSizeOptions={[25, 50, 100]} disableColumnSorting disableColumnFilter disableRowSelectionOnClick
          onRowClick={(p) => setSelected(p.row)}
          onCellKeyDown={(p, e) => e.key === 'Enter' && setSelected(p.row)}
          slots={{ noRowsOverlay: () => <EmptyState icon={<HistoryIcon />} title="Kayıt bulunamadı" description="Filtreleri değiştirerek tekrar deneyin." /> }}
          slotProps={{ loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' } }}
          sx={{ '--DataGrid-overlayHeight': '260px', '& .MuiDataGrid-row': { cursor: 'pointer' } }}
        />
      </Card>

      <Dialog open={!!selected} onClose={() => setSelected(null)} fullWidth maxWidth="sm">
        <Stack direction="row" sx={{ alignItems: 'center', pr: 1.5 }}>
          <DialogTitle sx={{ flex: 1, fontFamily: FONT_MONO, fontSize: 15 }}>{selected?.action}</DialogTitle>
          <IconButton onClick={() => setSelected(null)} aria-label="Kapat"><CloseIcon /></IconButton>
        </Stack>
        {selected && (
          <DialogContent sx={{ pb: 3 }}>
            <DetailList
              items={[
                { label: 'Zaman', value: dateTime(selected.createdAt) },
                { label: 'Kayıt türü', value: ENTITY_TYPES[selected.entityType] ?? selected.entityType },
                { label: 'Kullanıcı', value: actor(selected.userEmail) },
                { label: 'IP adresi', value: selected.ipAddress },
                { label: 'Korelasyon kimliği', value: selected.correlationId && <Box component="span" sx={mono}>{selected.correlationId}</Box> },
              ]}
            />
            <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mt: 3, mb: 1 }}>
              <Typography variant="overline" color="text.secondary">Ham kayıt</Typography>
              <CopyButton value={JSON.stringify(selected, null, 2)} label="JSON olarak kopyala" />
            </Stack>
            <Box component="pre" sx={{ ...mono, m: 0, p: 2, borderRadius: 2, overflow: 'auto', maxHeight: 320, bgcolor: 'background.default', border: 1, borderColor: 'divider' }}>
              {JSON.stringify(selected, null, 2)}
            </Box>
          </DialogContent>
        )}
      </Dialog>
    </>
  );
}
