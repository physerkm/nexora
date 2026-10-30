import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Box, Button, Card, Divider, InputAdornment, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { DataGrid } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/AddRounded';
import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import SearchIcon from '@mui/icons-material/SearchRounded';
import ClearIcon from '@mui/icons-material/FilterListOffRounded';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import { api, downloadFile } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { EmptyState, PageHeader, StatusChip, useDebounced } from '../components/Common';
import { useNotify } from '../components/Notify';
import { FONT_MONO } from '../theme';
import { CURRENCIES, dateTime, errorMessage, money, number, STATUS, TYPES } from '../utils/format';
import TransactionDrawer from './TransactionDrawer';
import NewTransactionDialog from './NewTransactionDialog';

// Only index-backed orders are offered (see TransactionReadRepository.Sort on the API).
const SORT_FIELDS = { createdAt: 'createdAt', amount: 'amount', status: 'status', reference: 'reference' };
const NO_FILTERS = { search: '', status: '', type: '', branchId: '', currency: '', from: '', to: '' };

const columns = [
  {
    field: 'reference', headerName: 'Referans', flex: 1.2, minWidth: 190,
    renderCell: (p) => <Box component="span" sx={{ fontFamily: FONT_MONO, fontSize: 13, fontWeight: 500 }}>{p.value}</Box>,
  },
  { field: 'createdAt', headerName: 'Tarih', flex: 1, minWidth: 140, valueFormatter: (v) => dateTime(v) },
  { field: 'branchName', headerName: 'Şube', flex: 1, minWidth: 130, sortable: false },
  { field: 'type', headerName: 'Tür', width: 120, sortable: false, valueFormatter: (v) => TYPES[v] ?? v },
  {
    field: 'counterpartyName', headerName: 'Karşı taraf', flex: 1, minWidth: 140, sortable: false,
    renderCell: (p) => p.value ?? <Box component="span" sx={{ color: 'text.disabled' }}>—</Box>,
  },
  {
    field: 'amount', headerName: 'Tutar', width: 150, align: 'right', headerAlign: 'right',
    renderCell: (p) => <Box component="span" sx={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>{money(p.value, p.row.currency)}</Box>,
  },
  {
    field: 'fee', headerName: 'Ücret', width: 110, align: 'right', headerAlign: 'right', sortable: false,
    renderCell: (p) => <Box component="span" sx={{ color: 'text.secondary', fontVariantNumeric: 'tabular-nums' }}>{money(p.value, p.row.currency)}</Box>,
  },
  { field: 'status', headerName: 'Durum', width: 140, renderCell: (p) => <StatusChip status={p.value} /> },
];

export default function TransactionsPage() {
  const { can, canWrite } = useAuth();
  const notify = useNotify();
  const [filters, setFilters] = useState(NO_FILTERS);
  const [pagination, setPagination] = useState({ page: 0, pageSize: 25 });
  const [sort, setSort] = useState([{ field: 'createdAt', sort: 'desc' }]);
  const [data, setData] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [exporting, setExporting] = useState(false);
  const [branches, setBranches] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [creating, setCreating] = useState(false);
  const search = useDebounced(filters.search);
  const latest = useRef(0);

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
    // Only the newest request may update the list; a slow earlier one must not overwrite it.
    const request = ++latest.current;
    setLoading(true);
    api('/transactions', { query })
      .then((r) => {
        if (request !== latest.current) return;
        setData(r);
        setError(null);
      })
      .catch((err) => request === latest.current && setError(err))
      .finally(() => request === latest.current && setLoading(false));
  }, [query]);

  useEffect(load, [load]);

  const setFilter = (key) => (e) => {
    setFilters((f) => ({ ...f, [key]: e.target.value }));
    setPagination((p) => ({ ...p, page: 0 }));
  };

  const filtered = Object.entries(filters).some(([, v]) => v !== '');
  const clearFilters = () => {
    setFilters(NO_FILTERS);
    setPagination((p) => ({ ...p, page: 0 }));
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page, pageSize, sortBy, sortDir, ...rest } = query;
      await downloadFile('/reports/transactions.csv', rest, `islemler-${new Date().toISOString().slice(0, 10)}.csv`);
    } catch (err) {
      notify(errorMessage(err), 'error');
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="İşlemler"
        subtitle={`${number(data.totalCount ?? 0)}${data.totalCountCapped ? '+' : ''} kayıt`}
        actions={
          <>
            {can('viewReports') && (
              <Button variant="outlined" color="inherit" startIcon={<DownloadIcon />} onClick={exportCsv} loading={exporting} loadingPosition="start">
                Dışa aktar
              </Button>
            )}
            {can('createTransactions') && canWrite && (
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>Yeni işlem</Button>
            )}
          </>
        }
      />

      {error && <Alert severity="error" sx={{ mb: 2 }}>{errorMessage(error)}</Alert>}

      <Card>
        {/* Two even rows on laptops (search + dates, then the four selects); one row on wide screens. */}
        <Box
          sx={{
            p: 2,
            display: 'grid',
            gap: 1.25,
            gridTemplateColumns: {
              xs: 'repeat(2, minmax(0, 1fr))',
              md: 'repeat(4, minmax(0, 1fr))',
              xl: 'minmax(240px, 2fr) repeat(6, minmax(0, 1fr))',
            },
          }}
        >
          <TextField
            size="small" placeholder="Referans, karşı taraf, açıklama" value={filters.search} onChange={setFilter('search')}
            sx={{ gridColumn: { xs: 'span 2', xl: 'auto' } }}
            slotProps={{
              htmlInput: { 'aria-label': 'Ara' },
              input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> },
            }}
          />
          <FilterSelect label="Durum" value={filters.status} onChange={setFilter('status')} options={Object.entries(STATUS).map(([k, v]) => [k, v.label])} />
          <FilterSelect label="Tür" value={filters.type} onChange={setFilter('type')} options={Object.entries(TYPES)} />
          <FilterSelect label="Şube" value={filters.branchId} onChange={setFilter('branchId')} options={branches.map((b) => [b.id, b.name])} />
          <FilterSelect label="Para birimi" value={filters.currency} onChange={setFilter('currency')} options={CURRENCIES.map((c) => [c, c])} />
          <TextField size="small" type="date" label="Başlangıç" value={filters.from} onChange={setFilter('from')} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField size="small" type="date" label="Bitiş" value={filters.to} onChange={setFilter('to')} slotProps={{ inputLabel: { shrink: true } }} />
        </Box>
        {filtered && (
          <Stack direction="row" spacing={1} sx={{ px: 2.5, pb: 1.5, mt: -0.5, alignItems: 'center' }}>
            <Typography variant="caption" color="text.secondary">Filtre uygulandı</Typography>
            <Button size="small" color="inherit" startIcon={<ClearIcon />} onClick={clearFilters} sx={{ color: 'text.secondary', minHeight: 26 }}>
              Filtreleri temizle
            </Button>
          </Stack>
        )}
        <Divider />
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
          onCellKeyDown={(p, e) => e.key === 'Enter' && setSelectedId(p.id)}
          slots={{
            noRowsOverlay: () => (
              <EmptyState
                icon={<ReceiptIcon />}
                title={filtered ? 'Sonuç bulunamadı' : 'Henüz işlem yok'}
                description={filtered ? 'Arama veya filtreleri değiştirerek tekrar deneyin.' : 'Oluşturulan işlemler burada listelenir.'}
              />
            ),
          }}
          slotProps={{ loadingOverlay: { variant: 'linear-progress', noRowsVariant: 'skeleton' } }}
          sx={{ '--DataGrid-overlayHeight': '280px', '& .MuiDataGrid-row': { cursor: 'pointer' } }}
        />
      </Card>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1.5 }}>
        Ayrıntıları görmek ve onay işlemleri için bir satıra tıklayın.
      </Typography>

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
    <TextField select size="small" label={label} value={value} onChange={onChange}>
      <MenuItem value="">Tümü</MenuItem>
      {options.map(([key, text]) => <MenuItem key={key} value={key}>{text}</MenuItem>)}
    </TextField>
  );
}
