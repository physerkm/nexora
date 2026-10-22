import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Box, Button, Divider, Drawer, IconButton, LinearProgress, List, ListItem, ListItemText, Stack, Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { api, newIdempotencyKey } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ReasonDialog, StatusChip } from '../components/Common';
import { useNotify } from '../components/Notify';
import { dateTime, errorMessage, money, TYPES } from '../utils/format';

const ACTION_LABELS = {
  'transaction.created': 'Oluşturuldu',
  'transaction.approved': 'Onaylandı',
  'transaction.rejected': 'Reddedildi',
  'transaction.cancelled': 'İptal edildi',
  'transaction.expired': 'Süresi doldu',
};

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

  return (
    <Drawer anchor="right" open={!!id} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 440 } } } }}>
      <Stack direction="row" sx={{ p: 2, alignItems: 'center' }}>
        <Typography variant="h6" sx={{ flex: 1 }}>İşlem detayı</Typography>
        <IconButton onClick={onClose} aria-label="Kapat"><CloseIcon /></IconButton>
      </Stack>
      <Divider />
      {!tx && !error && <LinearProgress />}
      {error && <Alert severity="error" sx={{ m: 2 }}>{errorMessage(error)}</Alert>}
      {tx && (
        <Box sx={{ p: 2, overflowY: 'auto' }}>
          <Stack spacing={0.5} sx={{ mb: 2 }}>
            <Typography variant="body2" sx={{ fontFamily: 'monospace' }} color="text.secondary">{tx.reference}</Typography>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Typography variant="h5" sx={{ fontVariantNumeric: 'tabular-nums' }}>{money(tx.amount, tx.currency)}</Typography>
              <StatusChip status={tx.status} />
            </Stack>
          </Stack>

          <Field label="Tür" value={TYPES[tx.type]} />
          <Field label="Şube" value={tx.branchName} />
          <Field label="Karşı taraf" value={tx.counterpartyName} />
          <Field label="Açıklama" value={tx.description} />
          <Field label="Ücret" value={money(tx.fee, tx.currency)} />
          <Field label="Net tutar" value={money(tx.netAmount, tx.currency)} />
          <Field label="Oluşturulma" value={dateTime(tx.createdAt)} />
          {tx.reviewedAt && <Field label="İnceleme" value={dateTime(tx.reviewedAt)} />}
          {tx.statusReason && <Field label="Gerekçe" value={tx.statusReason} />}

          {(canApprove || canReject || canCancel) && (
            <Stack direction="row" spacing={1} sx={{ my: 2 }}>
              {canApprove && <Button variant="contained" color="success" disabled={busy} onClick={() => act('approve')}>Onayla</Button>}
              {canReject && <Button variant="outlined" color="error" disabled={busy} onClick={() => setDialog('reject')}>Reddet</Button>}
              {canCancel && <Button color="inherit" disabled={busy} onClick={() => setDialog('cancel')}>İptal et</Button>}
            </Stack>
          )}
          {isOwn && allowed.includes('Approved') && reviewer && (
            <Alert severity="info" sx={{ my: 2 }}>Dört göz ilkesi: kendi oluşturduğunuz işlemi onaylayamazsınız.</Alert>
          )}

          <Typography variant="subtitle2" sx={{ mt: 3 }}>Geçmiş</Typography>
          <List dense>
            {history.length === 0 && (
              <Typography variant="body2" color="text.secondary">Bu işlem için denetim kaydı yok (demo verisi olarak yüklenmiş).</Typography>
            )}
            {history.map((h) => (
              <ListItem key={h.id} disableGutters>
                <ListItemText primary={ACTION_LABELS[h.action] ?? h.action}
                  secondary={`${dateTime(h.createdAt)} · ${h.userEmail ?? 'sistem'}${h.data?.reason ? ` · ${h.data.reason}` : ''}`} />
              </ListItem>
            ))}
          </List>
        </Box>
      )}

      <ReasonDialog
        open={!!dialog}
        busy={busy}
        title={dialog === 'reject' ? 'İşlemi reddet' : 'İşlemi iptal et'}
        confirmLabel={dialog === 'reject' ? 'Reddet' : 'İptal et'}
        onClose={() => setDialog(null)}
        onConfirm={(reason) => act(dialog, reason)}
      />
    </Drawer>
  );
}

function Field({ label, value }) {
  return (
    <Stack direction="row" sx={{ py: 0.75, borderBottom: 1, borderColor: 'divider' }}>
      <Typography variant="body2" color="text.secondary" sx={{ width: 120, flexShrink: 0 }}>{label}</Typography>
      <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>{value || '—'}</Typography>
    </Stack>
  );
}
