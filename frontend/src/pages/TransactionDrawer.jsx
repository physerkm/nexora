import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Box, Button, Divider, Drawer, IconButton, List, ListItem, ListItemText, Skeleton, Stack, Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/CloseRounded';
import { api, newIdempotencyKey } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { CopyButton, DetailList, ReasonDialog, StatusChip } from '../components/Common';
import { useNotify } from '../components/Notify';
import { FONT_MONO } from '../theme';
import { dateTime, errorMessage, money, TYPES } from '../utils/format';

const ACTIONS = {
  'transaction.created': { label: 'Oluşturuldu', color: 'primary.main' },
  'transaction.approved': { label: 'Onaylandı', color: 'success.main' },
  'transaction.rejected': { label: 'Reddedildi', color: 'error.main' },
  'transaction.cancelled': { label: 'İptal edildi', color: 'text.disabled' },
  'transaction.expired': { label: 'Süresi doldu', color: 'text.disabled' },
};

function SectionTitle({ children }) {
  return <Typography variant="overline" component="h3" color="text.secondary" sx={{ display: 'block', mb: 1 }}>{children}</Typography>;
}

export default function TransactionDrawer({ id, onClose, onChanged }) {
  const { user, can, canWrite } = useAuth();
  const notify = useNotify();
  const [tx, setTx] = useState(null);
  const [history, setHistory] = useState([]);
  const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState(null); // 'reject' | 'cancel'
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    if (!id) return;
    setError(null);
    try {
      const [detail, trail] = await Promise.all([api(`/transactions/${id}`), api(`/transactions/${id}/history`)]);
      setTx(detail);
      setHistory(trail);
    } catch (err) {
      setError(err);
    }
  }, [id]);

  useEffect(() => {
    setTx(null);
    load();
  }, [load]);

  const act = async (action, reason) => {
    setBusy(true);
    try {
      // The version makes the change conditional (optimistic concurrency); the key makes retries safe.
      const body = reason ? { version: tx.version, reason } : { version: tx.version };
      const updated = await api(`/transactions/${tx.id}/${action}`, { method: 'POST', body, idempotencyKey: newIdempotencyKey() });
      setTx(updated);
      setDialog(null);
      notify('İşlem güncellendi');
      onChanged();
      load();
    } catch (err) {
      if (err.code === 'concurrency_conflict') {
        notify('Kayıt bu arada başka biri tarafından değiştirildi; güncel hali yüklendi.', 'warning');
        load();
      } else {
        notify(errorMessage(err), 'error');
      }
    } finally {
      setBusy(false);
    }
  };

  const allowed = tx?.allowedTransitions ?? [];
  const isOwn = tx?.createdByUserId === user.id;
  const reviewer = can('reviewTransactions') && canWrite;
  const canApprove = reviewer && allowed.includes('Approved') && !isOwn;
  const canReject = reviewer && allowed.includes('Rejected') && !isOwn;
  const canCancel = canWrite && allowed.includes('Cancelled') &&
    (reviewer || (can('cancelTransactions') && isOwn && tx?.status === 'Pending'));
  const hasActions = canApprove || canReject || canCancel;

  return (
    <Drawer anchor="right" open={!!id} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, borderLeft: 1, borderColor: 'divider' } } }}>
      <Stack sx={{ height: '100%' }}>
        <Stack direction="row" sx={{ px: 3, py: 1.75, alignItems: 'center' }}>
          <Typography variant="h6" component="h2" sx={{ flex: 1 }}>İşlem detayı</Typography>
          <IconButton onClick={onClose} aria-label="Kapat" edge="end"><CloseIcon /></IconButton>
        </Stack>
        <Divider />

        {error && <Alert severity="error" sx={{ m: 3 }}>{errorMessage(error)}</Alert>}
        {!tx && !error && (
          <Stack spacing={1.5} sx={{ p: 3 }} aria-busy="true">
            <Skeleton width="45%" />
            <Skeleton variant="rounded" height={40} width="70%" />
            <Skeleton variant="rounded" height={220} sx={{ mt: 2 }} />
          </Stack>
        )}

        {tx && (
          <>
            <Box sx={{ flex: 1, overflowY: 'auto', px: 3, py: 3 }}>
              <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 0.5 }}>
                <Typography variant="body2" color="text.secondary" sx={{ fontFamily: FONT_MONO, fontSize: 13 }}>{tx.reference}</Typography>
                <CopyButton value={tx.reference} label="Referansı kopyala" />
              </Stack>
              <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>
                <Typography sx={{ fontSize: 30, fontWeight: 600, letterSpacing: '-0.02em', fontVariantNumeric: 'tabular-nums' }}>
                  {money(tx.amount, tx.currency)}
                </Typography>
                <StatusChip status={tx.status} />
              </Stack>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                {TYPES[tx.type]} · {tx.branchName}
              </Typography>

              {isOwn && allowed.includes('Approved') && reviewer && (
                <Alert severity="info" sx={{ mt: 3 }}>Dört göz ilkesi: kendi oluşturduğunuz işlemi onaylayamazsınız.</Alert>
              )}

              <Box sx={{ mt: 4 }}>
                <SectionTitle>Ayrıntılar</SectionTitle>
                <DetailList
                  items={[
                    { label: 'Karşı taraf', value: tx.counterpartyName },
                    { label: 'Açıklama', value: tx.description },
                    { label: 'Tutar', value: money(tx.amount, tx.currency) },
                    { label: 'Ücret', value: money(tx.fee, tx.currency) },
                    { label: 'Net tutar', value: money(tx.netAmount, tx.currency) },
                    { label: 'Oluşturulma', value: dateTime(tx.createdAt) },
                    tx.reviewedAt && { label: 'İnceleme', value: dateTime(tx.reviewedAt) },
                    tx.statusReason && { label: 'Gerekçe', value: tx.statusReason },
                  ]}
                />
              </Box>

              <Box sx={{ mt: 4 }}>
                <SectionTitle>Geçmiş</SectionTitle>
                {history.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">Bu işlem için denetim kaydı yok (demo verisi olarak yüklenmiş).</Typography>
                ) : (
                  <List disablePadding>
                    {history.map((h, i) => {
                      const meta = ACTIONS[h.action] ?? { label: h.action, color: 'text.disabled' };
                      const last = i === history.length - 1;
                      return (
                        <ListItem key={h.id} disableGutters alignItems="flex-start" sx={{ py: 0, gap: 1.5 }}>
                          <Stack sx={{ alignItems: 'center', alignSelf: 'stretch', pt: 0.75 }}>
                            <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: meta.color, boxShadow: (t) => `0 0 0 3px ${t.vars.palette.background.paper}` }} />
                            {!last && <Box sx={{ flex: 1, width: '1px', bgcolor: 'divider', mt: 0.5 }} />}
                          </Stack>
                          <Box sx={{ pb: last ? 0 : 2.5, minWidth: 0 }}>
                            <ListItemText
                              sx={{ m: 0 }}
                              primary={meta.label}
                              secondary={`${dateTime(h.createdAt)} · ${h.userEmail ?? 'sistem'}`}
                              slotProps={{ primary: { variant: 'body2', sx: { fontWeight: 600 } }, secondary: { variant: 'caption' } }}
                            />
                            {h.data?.reason && (
                              <Typography variant="body2" sx={{ mt: 0.75, px: 1.25, py: 0.75, borderRadius: 1.5, bgcolor: 'action.hover', color: 'text.secondary' }}>
                                {h.data.reason}
                              </Typography>
                            )}
                          </Box>
                        </ListItem>
                      );
                    })}
                  </List>
                )}
              </Box>
            </Box>

            {hasActions && (
              <Stack direction="row" spacing={1} sx={{ px: 3, py: 2, borderTop: 1, borderColor: 'divider', bgcolor: 'background.default' }}>
                {canCancel && (
                  <Button variant="outlined" color="inherit" disabled={busy} onClick={() => setDialog('cancel')}>
                    İptal et
                  </Button>
                )}
                <Box sx={{ flex: 1 }} />
                {canReject && <Button variant="outlined" color="error" disabled={busy} onClick={() => setDialog('reject')}>Reddet</Button>}
                {canApprove && <Button variant="contained" color="success" disabled={busy} onClick={() => act('approve')}>Onayla</Button>}
              </Stack>
            )}
          </>
        )}
      </Stack>

      <ReasonDialog
        open={!!dialog}
        busy={busy}
        title={dialog === 'reject' ? 'İşlemi reddet' : 'İşlemi iptal et'}
        description={dialog === 'reject'
          ? 'Reddedilen işlem yeniden onaya açılamaz.'
          : 'İptal edilen işlem geri alınamaz.'}
        confirmLabel={dialog === 'reject' ? 'Reddet' : 'İptal et'}
        onClose={() => setDialog(null)}
        onConfirm={(reason) => act(dialog, reason)}
      />
    </Drawer>
  );
}
