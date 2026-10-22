import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Button, Card, InputAdornment, MenuItem, Stack, TextField } from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import SearchIcon from '@mui/icons-material/Search';
import { api, downloadFile } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { PageHeader, StatusChip, useDebounced } from '../components/Common';
import { useNotify } from '../components/Notify';
import { CURRENCIES, dateTime, errorMessage, money, STATUS, TYPES } from '../utils/format';
import TransactionDrawer from './TransactionDrawer';
import NewTransactionDialog from './NewTransactionDialog';

const SORT_FIELDS = { createdAt: 'createdAt', amount: 'amount', status: 'status', reference: 'reference', type: 'type' };

export default function TransactionsPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [filters, setFilters] = useState({ search: '', status: '', type: '', branchId: '', currency: '', from: '', to: '' });
  const [pagination, setPagination] = useState({ page: 0, pageSize: 25 });
  const [sort, setSort] = useState([{ field: 'createdAt', sort: 'desc' }]);
  const [data, setData] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [branches, setBranches] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [creating, setCreating] = useState(false);
  const search = useDebounced(filters.search);

  useEffect(() => {
    api('/branches', { query: { pageSize: 100 } }).then((r) => setBranches(r.items)).catch(() => {});
  }, []);

  const query = useMemo(() => ({
    page: pagination.page + 1,
    pageSize: pagination.pageSize,
    sortBy: SORT_FIELDS[sort[0]?.field] ?? 'createdAt',
    sortDir: sort[0]?.sort ?? 'desc',
    search,
    status: filters.status,
    type: filters.type,
    branchId: filters.branchId,
    currency: filters.currency,
    from: filters.from ? new Date(filters.from).toISOString() : '',
    to: filters.to ? new Date(`${filters.to}T23:59:59`).toISOString() : '',
  }), [pagination, sort, search, filters.status, filters.type, filters.branchId, filters.currency, filters.from, filters.to]);

  const load = useCallback(() => {
    setLoading(true);
    api('/transactions', { query })
      .then((r) => {
        setData(r);
        setError(null);
      })
      .catch(setError)
      .finally(() => setLoading(false));
  }, [query]);

  useEffect(load, [load]);

  const setFilter = (key) => (e) => {
    setFilters((f) => ({ ...f, [key]: e.target.value }));
    setPagination((p) => ({ ...p, page: 0 }));
  };

  const exportCsv = async () => {
    try {
      const { page, pageSize, sortBy, sortDir, ...rest } = query;
      await downloadFile('/reports/transactions.csv', rest, `islemler-${new Date().toISOString().slice(0, 10)}.csv`);
    } catch (err) {
      notify(errorMessage(err), 'error');
    }
  };

  const columns = [
    { field: 'reference', headerName: 'Referans', flex: 1.2, minWidth: 190, cellClassName: 'mono' },
    { field: 'createdAt', headerName: 'Tarih', flex: 1, minWidth: 140, valueFormatter: (v) => dateTime(v) },
    { field: 'branchName', headerName: 'Şube', flex: 1, minWidth: 130, sortable: false },
    { field: 'type', headerName: 'Tür', width: 120, valueFormatter: (v) => TYPES[v] ?? v },
    { field: 'counterpartyName', headerName: 'Karşı taraf', flex: 1, minWidth: 140, sortable: false },
    { field: 'amount', headerName: 'Tutar', width: 140, align: 'right', headerAlign: 'right', renderCell: (p) => money(p.value, p.row.currency) },
    { field: 'fee', headerName: 'Ücret', width: 110, align: 'right', headerAlign: 'right', sortable: false, renderCell: (p) => money(p.value, p.row.currency) },
    { field: 'status', headerName: 'Durum', width: 130, renderCell: (p) => <StatusChip status={p.value} /> },
  ];

  return (
    <>
      <PageHeader
        title="İşlemler"
        subtitle={`${data.totalCount ?? 0} kayıt`}
        actions={
          <>
            {can('viewReports') && <Button startIcon={<DownloadIcon />} onClick={exportCsv}>CSV</Button>}
            {can('createTransactions') && canWrite && (
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>Yeni işlem</Button>
            )}
          </>
        }
      />

      <Card sx={{ p: 2, mb: 2 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <TextField size="small" placeholder="Referans, karşı taraf, açıklama" value={filters.search} onChange={setFilter('search')} sx={{ minWidth: 260, flex: 1 }}
            slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
          <FilterSelect label="Durum" value={filters.status} onChange={setFilter('status')} options={Object.entries(STATUS).map(([k, v]) => [k, v.label])} />
          <FilterSelect label="Tür" value={filters.type} onChange={setFilter('type')} options={Object.entries(TYPES)} />
          <FilterSelect label="Şube" value={filters.branchId} onChange={setFilter('branchId')} options={branches.map((b) => [b.id, b.name])} />
          <FilterSelect label="Para birimi" value={filters.currency} onChange={setFilter('currency')} options={CURRENCIES.map((c) => [c, c])} />
          <TextField size="small" type="date" label="Başlangıç" value={filters.from} onChange={setFilter('from')} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField size="small" type="date" label="Bitiş" value={filters.to} onChange={setFilter('to')} slotProps={{ inputLabel: { shrink: true } }} />
        </Stack>
      </Card>

      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}

      <Card>
        <DataGrid
          autoHeight
          rows={data.items}
          columns={columns}
          rowCount={data.totalCount ?? 0}
          loading={loading}
          paginationMode="server"
          sortingMode="server"
          paginationModel={pagination}
          onPaginationModelChange={setPagination}
          sortModel={sort}
          onSortModelChange={(m) => setSort(m.length ? m : [{ field: 'createdAt', sort: 'desc' }])}
          pageSizeOptions={[10, 25, 50, 100]}
          disableColumnFilter
          disableRowSelectionOnClick
          onRowClick={(p) => setSelectedId(p.id)}
          sx={{ border: 0, '& .MuiDataGrid-row': { cursor: 'pointer' }, '& .mono': { fontFamily: 'ui-monospace, monospace', fontSize: 13 } }}
        />
      </Card>

      <TransactionDrawer id={selectedId} onClose={() => setSelectedId(null)} onChanged={load} />
      <NewTransactionDialog open={creating} branches={branches} onClose={() => setCreating(false)}
        onCreated={(tx) => {
          setCreating(false);
          notify(`${tx.reference} oluşturuldu`);
          load();
        }} />
    </>
  );
}

function FilterSelect({ label, value, onChange, options }) {
  return (
    <TextField select size="small" label={label} value={value} onChange={onChange} sx={{ minWidth: 140 }}>
      <MenuItem value="">Tümü</MenuItem>
      {options.map(([key, text]) => <MenuItem key={key} value={key}>{text}</MenuItem>)}
    </TextField>
  );
}
