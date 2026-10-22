import { useEffect, useState } from 'react';
import {
  Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography,
} from '@mui/material';
import { STATUS } from '../utils/format';

export function PageHeader({ title, subtitle, actions }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 3, alignItems: { sm: 'center' }, justifyContent: 'space-between' }}>
      <Box>
        <Typography variant="h5">{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary">{subtitle}</Typography>}
      </Box>
      {actions && <Stack direction="row" spacing={1}>{actions}</Stack>}
    </Stack>
  );
}

export function StatusChip({ status }) {
  const meta = STATUS[status] ?? { label: status, color: 'default' };
  return <Chip size="small" label={meta.label} color={meta.color} variant={status === 'Pending' ? 'filled' : 'outlined'} />;
}

export function ReasonDialog({ open, title, confirmLabel, onClose, onConfirm, busy }) {
  const [reason, setReason] = useState('');
  useEffect(() => {
    if (open) setReason('');
  }, [open]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <TextField autoFocus fullWidth multiline minRows={2} margin="dense" label="Gerekçe" value={reason}
          onChange={(e) => setReason(e.target.value)} slotProps={{ htmlInput: { maxLength: 500 } }} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Vazgeç</Button>
        <Button variant="contained" color="error" disabled={!reason.trim() || busy} onClick={() => onConfirm(reason.trim())}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function useDebounced(value, delay = 400) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return debounced;
}
