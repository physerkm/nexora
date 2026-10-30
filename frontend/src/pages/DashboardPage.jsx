import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Box, Card, CardContent, Grid, LinearProgress, Skeleton, Stack, Table, TableBody, TableCell, TableHead, TableRow,
  ToggleButton, ToggleButtonGroup, Tooltip, Typography, useColorScheme,
} from '@mui/material';
import { BarChart } from '@mui/x-charts/BarChart';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import WalletIcon from '@mui/icons-material/AccountBalanceWalletOutlined';
import StoreIcon from '@mui/icons-material/StorefrontOutlined';
import InsightsIcon from '@mui/icons-material/InsightsOutlined';
import { api } from '../api/client';
import { EmptyState, PageHeader, Panel } from '../components/Common';
import { dateTime, money, number, percent, shortDate, STATUS } from '../utils/format';

const PERIODS = [7, 30, 90];
// Fixed order, so a status keeps its place (and colour) whatever its count.
const STATUS_ORDER = ['Approved', 'Pending', 'Rejected', 'Cancelled', 'Expired'];

// Chart colours are resolved per mode: the chart library needs concrete values, not CSS variables.
const SERIES = {
  light: { approved: '#4f46e5', other: '#d4d4d8' },
  dark: { approved: '#818cf8', other: '#3f3f46' },
};

export default function DashboardPage() {
  const [days, setDays] = useState(30);
  const [summary, setSummary] = useState(null);
  const [meta, setMeta] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let current = true; // ignore a slower response for a period the user already left
    const now = new Date();
    const from = new Date(now.getTime() - days * 86400000);
    setLoading(true);
    setError(null);
    api('/reports/summary', { query: { from: from.toISOString(), to: now.toISOString() }, raw: true })
      .then(async (res) => {
        if (!res.ok) throw new Error((await res.json()).detail);
        const body = await res.json();
        if (!current) return;
        setMeta({ cache: res.headers.get('X-Cache'), timing: res.headers.get('Server-Timing') });
        setSummary(body);
      })
      .catch((err) => current && setError(err))
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
  }, [days]);

  const stats = useMemo(() => {
    if (!summary) return null;
    const total = summary.statusBreakdown.reduce((s, x) => s + x.count, 0);
    const approved = summary.statusBreakdown.find((s) => s.status === 'Approved')?.count ?? 0;
    // The API lists currencies alphabetically; there is room for three cards, so show the busiest ones.
    const currencies = [...summary.totals].sort((a, b) => b.approvedCount - a.approvedCount).slice(0, 3);
    return { total, approved, currencies };
  }, [summary]);

  const range = summary ? `${shortDate(summary.from)} – ${shortDate(summary.to)}` : null;

  return (
    <>
      <PageHeader
        title="Gösterge paneli"
        subtitle={range ? `Son ${days} gün · ${range}` : `Son ${days} gün`}
        actions={
          <ToggleButtonGroup size="small" exclusive value={days} onChange={(_, v) => v && setDays(v)} aria-label="Dönem">
            {PERIODS.map((p) => <ToggleButton key={p} value={p}>{p} gün</ToggleButton>)}
          </ToggleButtonGroup>
        }
      />
      {error && <Alert severity="error" sx={{ mb: 3 }}>{error.message}</Alert>}
      {!summary && !error && <DashboardSkeleton />}
      {summary && (
        <Stack spacing={2.5} sx={{ opacity: loading ? 0.6 : 1, transition: 'opacity 150ms' }} aria-busy={loading}>
          <Grid container spacing={2.5}>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <Kpi
                icon={<ReceiptIcon />}
                label="Toplam işlem"
                value={number(stats.total)}
                hint={`${number(stats.approved)} onaylı · ${percent(stats.approved, stats.total)} onay oranı`}
              />
            </Grid>
            {stats.currencies.map((t) => (
              <Grid key={t.currency} size={{ xs: 12, sm: 6, lg: 3 }}>
                <Kpi
                  icon={<WalletIcon />}
                  label={`Onaylı hacim · ${t.currency}`}
                  value={money(t.approvedVolume, t.currency)}
                  hint={`${number(t.approvedCount)} işlem · ${money(t.feeIncome, t.currency)} ücret geliri`}
                />
              </Grid>
            ))}
          </Grid>

          <Grid container spacing={2.5}>
            <Grid size={{ xs: 12, lg: 8 }}>
              <DailyChart daily={summary.daily} />
            </Grid>
            <Grid size={{ xs: 12, lg: 4 }}>
              <StatusBreakdown items={summary.statusBreakdown} total={stats.total} />
            </Grid>
          </Grid>

          <BranchTable branches={summary.branches} />

          {meta && (
            <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end', color: 'text.secondary' }}>
              <Typography variant="caption">Rapor zamanı: {dateTime(summary.generatedAt)}</Typography>
              <Typography variant="caption">{`Önbellek: ${meta.cache} · ${meta.timing?.replace('report;dur=', '')} ms`}</Typography>
            </Stack>
          )}
        </Stack>
      )}
    </>
  );
}

function Kpi({ icon, label, value, hint }) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1.5 }}>
          <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 500 }}>{label}</Typography>
          <Box sx={{ color: 'text.secondary', display: 'flex', '& svg': { fontSize: 18 } }}>{icon}</Box>
        </Stack>
        <Typography sx={{ fontSize: 26, fontWeight: 600, letterSpacing: '-0.02em', lineHeight: 1.2, fontVariantNumeric: 'tabular-nums' }}>
          {value}
        </Typography>
        {hint && <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 0.75 }}>{hint}</Typography>}
      </CardContent>
    </Card>
  );
}

function LegendItem({ color, label }) {
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
      <Box sx={{ width: 10, height: 10, borderRadius: '3px', bgcolor: color }} />
      <Typography variant="caption" color="text.secondary">{label}</Typography>
    </Stack>
  );
}

function DailyChart({ daily }) {
  const { mode, systemMode } = useColorScheme();
  const colors = SERIES[(mode === 'system' ? systemMode : mode) === 'dark' ? 'dark' : 'light'];
  const empty = daily.every((d) => d.count === 0);

  return (
    <Panel
      title="Günlük işlem adedi"
      description="Onaylanan ve diğer durumlardaki işlemler"
      sx={{ height: '100%' }}
      action={!empty && (
        <Stack direction="row" spacing={2} sx={{ pt: 0.5 }}>
          <LegendItem color={colors.approved} label="Onaylı" />
          <LegendItem color={colors.other} label="Diğer" />
        </Stack>
      )}
    >
      {empty ? (
        <EmptyState icon={<InsightsIcon />} title="Bu dönemde işlem yok" description="Farklı bir dönem seçerek tekrar deneyin." />
      ) : (
        <BarChart
          height={280}
          hideLegend
          borderRadius={4}
          grid={{ horizontal: true }}
          margin={{ left: 0, right: 8, top: 8, bottom: 0 }}
          xAxis={[{
            scaleType: 'band',
            data: daily.map((d) => d.date),
            valueFormatter: (v) => shortDate(`${v}T00:00:00`),
            categoryGapRatio: 0.35,
            disableLine: true,
            disableTicks: true,
          }]}
          yAxis={[{ disableLine: true, disableTicks: true, width: 40, valueFormatter: (v) => number(v) }]}
          series={[
            { data: daily.map((d) => d.approvedCount), label: 'Onaylı', stack: 'a', color: colors.approved, valueFormatter: (v) => number(v) },
            { data: daily.map((d) => d.count - d.approvedCount), label: 'Diğer', stack: 'a', color: colors.other, valueFormatter: (v) => number(v) },
          ]}
          sx={{
            '& .MuiChartsAxis-tickLabel': { fill: 'var(--mui-palette-text-secondary) !important', fontSize: '11px !important' },
            '& .MuiChartsGrid-line': { stroke: 'var(--mui-palette-divider)' },
          }}
        />
      )}
    </Panel>
  );
}

function StatusBreakdown({ items, total }) {
  const byStatus = Object.fromEntries(items.map((s) => [s.status, s.count]));
  const rows = STATUS_ORDER.filter((s) => byStatus[s] > 0).map((s) => ({ status: s, count: byStatus[s], ...STATUS[s] }));

  return (
    <Panel title="Durum dağılımı" description={`${number(total)} işlem`} sx={{ height: '100%' }}>
      {rows.length === 0 ? (
        <EmptyState title="Veri yok" />
      ) : (
        <>
          <Box
            role="img"
            aria-label={rows.map((r) => `${r.label} ${percent(r.count, total)}`).join(', ')}
            sx={{ display: 'flex', gap: '2px', height: 10, borderRadius: 5, overflow: 'hidden', mb: 2.5 }}
          >
            {rows.map((r) => (
              <Tooltip key={r.status} title={`${r.label}: ${number(r.count)} (${percent(r.count, total)})`}>
                <Box sx={{ flexGrow: r.count, flexBasis: 0, minWidth: 4, bgcolor: r.chart }} />
              </Tooltip>
            ))}
          </Box>
          <Stack divider={<Box sx={{ borderTop: 1, borderColor: 'divider' }} />}>
            {rows.map((r) => (
              <Stack key={r.status} direction="row" spacing={1.25} sx={{ alignItems: 'center', py: 1.1 }}>
                <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: r.chart, flexShrink: 0 }} />
                <Typography variant="body2" sx={{ flex: 1 }}>{r.label}</Typography>
                <Typography variant="body2" sx={{ fontWeight: 500, fontVariantNumeric: 'tabular-nums' }}>{number(r.count)}</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ width: 44, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                  {percent(r.count, total)}
                </Typography>
              </Stack>
            ))}
          </Stack>
        </>
      )}
    </Panel>
  );
}

function BranchTable({ branches }) {
  const sorted = [...branches].sort((a, b) => b.count - a.count);
  return (
    <Panel title="Şube performansı" description="İşlem adedine göre sıralı" flush>
      {sorted.length === 0 ? (
        <EmptyState icon={<StoreIcon />} title="Şube verisi yok" description="Bu dönemde hiçbir şubede işlem yapılmamış." />
      ) : (
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" sx={{ '& td, & th': { px: 2.5 }, '& tr:last-child td': { borderBottom: 0 } }}>
            <TableHead>
              <TableRow>
                <TableCell>Şube</TableCell>
                <TableCell align="right">İşlem</TableCell>
                <TableCell align="right">Onaylı</TableCell>
                <TableCell sx={{ width: { md: 280 } }}>Onay oranı</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {sorted.map((b) => {
                const rate = b.count ? Math.round((b.approvedCount / b.count) * 100) : 0;
                return (
                  <TableRow key={b.branchId} hover>
                    <TableCell sx={{ fontWeight: 500, py: 1.5 }}>{b.branchName}</TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{number(b.count)}</TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{number(b.approvedCount)}</TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
                        <LinearProgress
                          variant="determinate" value={rate} aria-label={`${b.branchName} onay oranı`}
                          sx={{ flex: 1, height: 6, bgcolor: 'action.hover' }}
                        />
                        <Typography variant="body2" sx={{ width: 40, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                          {b.count ? `%${rate}` : '—'}
                        </Typography>
                      </Stack>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Box>
      )}
    </Panel>
  );
}

function DashboardSkeleton() {
  return (
    <Stack spacing={2.5} aria-busy="true" aria-label="Yükleniyor">
      <Grid container spacing={2.5}>
        {[0, 1, 2, 3].map((i) => (
          <Grid key={i} size={{ xs: 12, sm: 6, lg: 3 }}><Skeleton variant="rounded" height={116} /></Grid>
        ))}
      </Grid>
      <Grid container spacing={2.5}>
        <Grid size={{ xs: 12, lg: 8 }}><Skeleton variant="rounded" height={360} /></Grid>
        <Grid size={{ xs: 12, lg: 4 }}><Skeleton variant="rounded" height={360} /></Grid>
      </Grid>
      <Skeleton variant="rounded" height={240} />
    </Stack>
  );
}
