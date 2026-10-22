import { useEffect, useState } from 'react';
import {
  Alert, Card, CardContent, Grid, LinearProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow,
  ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material';
import { BarChart } from '@mui/x-charts/BarChart';
import { PieChart } from '@mui/x-charts/PieChart';
import { api } from '../api/client';
import { PageHeader } from '../components/Common';
import { money, number, STATUS } from '../utils/format';

const STATUS_COLORS = { Pending: '#f59e0b', Approved: '#16a34a', Rejected: '#dc2626', Cancelled: '#64748b', Expired: '#7c3aed' };

export default function DashboardPage() {
  const [days, setDays] = useState(30);
  const [summary, setSummary] = useState(null);
  const [meta, setMeta] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    const now = new Date();
    const from = new Date(now.getTime() - days * 86400000);
    api('/reports/summary', { query: { from: from.toISOString(), to: now.toISOString() }, raw: true })
      .then(async (res) => {
        if (!res.ok) throw new Error((await res.json()).detail);
        setMeta({ cache: res.headers.get('X-Cache'), timing: res.headers.get('Server-Timing') });
        setSummary(await res.json());
      })
      .catch(setError);
  }, [days]);

  const total = summary?.statusBreakdown.reduce((s, x) => s + x.count, 0) ?? 0;

  return (
    <>
      <PageHeader
        title="Gösterge paneli"
        subtitle={meta ? `Önbellek: ${meta.cache} · ${meta.timing?.replace('report;dur=', '')} ms` : 'Yükleniyor…'}
        actions={
          <ToggleButtonGroup size="small" exclusive value={days} onChange={(_, v) => v && setDays(v)}>
            <ToggleButton value={7}>7 gün</ToggleButton>
            <ToggleButton value={30}>30 gün</ToggleButton>
            <ToggleButton value={90}>90 gün</ToggleButton>
          </ToggleButtonGroup>
        }
      />
      {error && <Alert severity="error">{error.message}</Alert>}
      {!summary && !error && <LinearProgress />}
      {summary && (
        <Stack spacing={3}>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <Kpi label="Toplam işlem" value={number(total)} />
            </Grid>
            {summary.totals.slice(0, 3).map((t) => (
              <Grid key={t.currency} size={{ xs: 12, sm: 6, lg: 3 }}>
                <Kpi label={`Onaylı hacim · ${t.currency}`} value={money(t.approvedVolume, t.currency)}
                  hint={`${number(t.approvedCount)} işlem · ücret geliri ${money(t.feeIncome, t.currency)}`} />
              </Grid>
            ))}
          </Grid>

          <Grid container spacing={2}>
            <Grid size={{ xs: 12, md: 8 }}>
              <Card>
                <CardContent>
                  <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>Günlük işlem adedi</Typography>
                  <BarChart
                    height={300}
                    xAxis={[{ scaleType: 'band', data: summary.daily.map((d) => d.date.slice(5)) }]}
                    series={[
                      { data: summary.daily.map((d) => d.approvedCount), label: 'Onaylı', stack: 'a', color: STATUS_COLORS.Approved },
                      { data: summary.daily.map((d) => d.count - d.approvedCount), label: 'Diğer', stack: 'a', color: '#94a3b8' },
                    ]}
                  />
                </CardContent>
              </Card>
            </Grid>
            <Grid size={{ xs: 12, md: 4 }}>
              <Card sx={{ height: '100%' }}>
                <CardContent>
                  <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>Durum dağılımı</Typography>
                  <PieChart
                    height={260}
                    series={[{
                      innerRadius: 50,
                      paddingAngle: 2,
                      cornerRadius: 4,
                      data: summary.statusBreakdown.map((s) => ({ id: s.status, value: s.count, label: STATUS[s.status]?.label, color: STATUS_COLORS[s.status] })),
                    }]}
                  />
                </CardContent>
              </Card>
            </Grid>
          </Grid>

          <Card>
            <CardContent>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, mb: 1 }}>Şubeler</Typography>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Şube</TableCell>
                    <TableCell align="right">İşlem</TableCell>
                    <TableCell align="right">Onaylı</TableCell>
                    <TableCell align="right">Onay oranı</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {summary.branches.map((b) => (
                    <TableRow key={b.branchId}>
                      <TableCell>{b.branchName}</TableCell>
                      <TableCell align="right">{number(b.count)}</TableCell>
                      <TableCell align="right">{number(b.approvedCount)}</TableCell>
                      <TableCell align="right">{b.count ? `%${Math.round((b.approvedCount / b.count) * 100)}` : '—'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Stack>
      )}
    </>
  );
}

function Kpi({ label, value, hint }) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="body2" color="text.secondary">{label}</Typography>
        <Typography variant="h5" sx={{ mt: 0.5, fontVariantNumeric: 'tabular-nums' }}>{value}</Typography>
        {hint && <Typography variant="caption" color="text.secondary">{hint}</Typography>}
      </CardContent>
    </Card>
  );
}
