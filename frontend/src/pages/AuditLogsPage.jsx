import { useEffect, useMemo, useState } from 'react';
import { Box, Card, Dialog, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import { api } from '../api/client';
import { PageHeader, useDebounced } from '../components/Common';
import { dateTime } from '../utils/format';

const ENTITY_TYPES = ['Transaction', 'Branch', 'User', 'FeeRule', 'Tenant'];

export default function AuditLogsPage() {
  const [pagination, setPagination] = useState({ page: 0, pageSize: 25 });
  const [entityType, setEntityType] = useState('');
  const [action, setAction] = useState('');
  const [data, setData] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState(null);
  const debouncedAction = useDebounced(action);

  const query = useMemo(() => ({ page: pagination.page + 1, pageSize: pagination.pageSize, entityType, action: debouncedAction }),
    [pagination, entityType, debouncedAction]);

  useEffect(() => {
    setLoading(true);
    api('/audit-logs', { query }).then(setData).finally(() => setLoading(false));
  }, [query]);

  const columns = [
    { field: 'createdAt', headerName: 'Zaman', width: 150, valueFormatter: (v) => dateTime(v) },
    { field: 'action', headerName: 'Eylem', flex: 1, minWidth: 200, renderCell: (p) => <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{p.value}</Typography> },
    { field: 'entityType', headerName: 'Varlık', width: 120 },
    { field: 'userEmail', headerName: 'Kullanıcı', flex: 1, minWidth: 180 },
    { field: 'ipAddress', headerName: 'IP', width: 130 },
    { field: 'correlationId', headerName: 'Korelasyon', width: 140, valueFormatter: (v) => (v ? `${v.slice(0, 10)}…` : '') },
  ];

  return (
    <>
      <PageHeader title="Denetim kayıtları" subtitle="Değiştirilemez kayıt: veritabanı güncelleme ve silmeyi reddeder." />
      <Card sx={{ p: 2, mb: 2 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
          <TextField select size="small" label="Varlık" value={entityType} sx={{ minWidth: 160 }}
            onChange={(e) => { setEntityType(e.target.value); setPagination((p) => ({ ...p, page: 0 })); }}>
            <MenuItem value="">Tümü</MenuItem>
            {ENTITY_TYPES.map((t) => <MenuItem key={t} value={t}>{t}</MenuItem>)}
          </TextField>
          <TextField size="small" label="Eylem öneki (ör. transaction.)" value={action}
            onChange={(e) => { setAction(e.target.value); setPagination((p) => ({ ...p, page: 0 })); }} />
        </Stack>
      </Card>
      <Card>
        <DataGrid autoHeight rows={data.items} columns={columns} rowCount={data.totalCount ?? 0} loading={loading}
          paginationMode="server" paginationModel={pagination} onPaginationModelChange={setPagination}
          pageSizeOptions={[25, 50, 100]} disableColumnSorting disableColumnFilter disableRowSelectionOnClick
          onRowClick={(p) => setSelected(p.row)} sx={{ border: 0, '& .MuiDataGrid-row': { cursor: 'pointer' } }} />
      </Card>
      <Dialog open={!!selected} onClose={() => setSelected(null)} fullWidth maxWidth="sm">
        <DialogTitle>{selected?.action}</DialogTitle>
        <DialogContent>
          <Box component="pre" sx={{ m: 0, p: 2, bgcolor: 'action.hover', borderRadius: 2, overflow: 'auto', fontSize: 13 }}>
            {JSON.stringify(selected, null, 2)}
          </Box>
        </DialogContent>
      </Dialog>
    </>
  );
}
