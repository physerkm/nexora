import { useEffect, useState } from 'react';
import {
  Box, Button, Card, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, IconButton, Stack,
  TextField, Tooltip, Typography,
} from '@mui/material';
import CopyIcon from '@mui/icons-material/ContentCopyRounded';
import CheckIcon from '@mui/icons-material/CheckRounded';
import { STATUS } from '../utils/format';
import { tint } from '../theme';

/** Sets the browser tab title for the current page. */
export function useDocumentTitle(title) {
  useEffect(() => {
    const previous = document.title;
    document.title = title ? `${title} · Nexora` : 'Nexora';
    return () => {
      document.title = previous;
    };
  }, [title]);
}

export function useDebounced(value, delay = 400) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return debounced;
}

export function PageHeader({ title, subtitle, actions }) {
  useDocumentTitle(title);
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      sx={{ mb: { xs: 2.5, md: 3 }, alignItems: { sm: 'flex-end' }, justifyContent: 'space-between' }}
    >
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="h4" component="h1">{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{subtitle}</Typography>}
      </Box>
      {actions && (
        <Stack direction="row" useFlexGap sx={{ gap: 1, flexWrap: 'wrap', flexShrink: 0 }}>{actions}</Stack>
      )}
    </Stack>
  );
}

/** A card with an optional heading row; `flush` lets tables and grids run edge to edge. */
export function Panel({ title, description, action, flush = false, children, sx }) {
  const hasHeader = title || action;
  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', ...sx }}>
      {hasHeader && (
        <Stack direction="row" spacing={2} sx={{ px: 2.5, pt: 2.25, pb: flush ? 2 : 0, alignItems: 'flex-start' }}>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            {title && <Typography variant="subtitle1" component="h2">{title}</Typography>}
            {description && <Typography variant="body2" color="text.secondary">{description}</Typography>}
          </Box>
          {action}
        </Stack>
      )}
      {flush ? children : <Box sx={{ p: 2.5, pt: hasHeader ? 2 : 2.5, flex: 1 }}>{children}</Box>}
    </Card>
  );
}

const Dot = () => <Box component="span" sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'currentColor' }} />;

/** Soft status badge. `tone` is a palette colour (success, warning, error, info, primary) or `neutral`. */
export function Pill({ tone = 'neutral', children, dot = true }) {
  return (
    <Chip
      size="small"
      label={children}
      icon={dot ? <Dot /> : undefined}
      sx={(theme) => ({
        '& .MuiChip-icon': { color: 'inherit', ml: '8px', mr: '-2px' },
        ...(tone === 'neutral'
          ? { color: theme.vars.palette.text.secondary, bgcolor: theme.vars.palette.action.hover, border: `1px solid ${theme.vars.palette.divider}` }
          : {
            color: theme.vars.palette[tone].dark,
            bgcolor: tint(theme, tone, 0.1),
            border: `1px solid ${tint(theme, tone, 0.22)}`,
            ...theme.applyStyles('dark', { color: theme.vars.palette[tone].main }),
          }),
      })}
    />
  );
}

export function StatusChip({ status }) {
  const meta = STATUS[status] ?? { label: status, tone: 'neutral' };
  return <Pill tone={meta.tone}>{meta.label}</Pill>;
}

export function ActivePill({ active }) {
  return <Pill tone={active ? 'success' : 'neutral'}>{active ? 'Aktif' : 'Pasif'}</Pill>;
}

export function EmptyState({ icon, title, description, action }) {
  return (
    <Stack spacing={0.75} sx={{ alignItems: 'center', textAlign: 'center', py: 6, px: 3, height: '100%', justifyContent: 'center' }}>
      {icon && (
        <Box
          sx={{
            width: 44, height: 44, mb: 1, borderRadius: 3, display: 'grid', placeItems: 'center',
            color: 'text.secondary', bgcolor: 'action.hover', border: 1, borderColor: 'divider',
          }}
        >
          {icon}
        </Box>
      )}
      <Typography variant="subtitle1">{title}</Typography>
      {description && <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 380 }}>{description}</Typography>}
      {action && <Box sx={{ pt: 1.5 }}>{action}</Box>}
    </Stack>
  );
}

/** Label/value rows rendered as a description list. */
export function DetailList({ items }) {
  return (
    <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'minmax(110px, 35%) 1fr' }}>
      {items.filter(Boolean).map(({ label, value }) => (
        <Box key={label} sx={{ display: 'contents', '& > *': { py: 1.1, borderBottom: 1, borderColor: 'divider' } }}>
          <Typography component="dt" variant="body2" color="text.secondary">{label}</Typography>
          <Typography component="dd" variant="body2" sx={{ m: 0, pl: 2, wordBreak: 'break-word', fontVariantNumeric: 'tabular-nums' }}>
            {value || '—'}
          </Typography>
        </Box>
      ))}
    </Box>
  );
}

export function CopyButton({ value, label = 'Kopyala' }) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return undefined;
    const id = setTimeout(() => setCopied(false), 1500);
    return () => clearTimeout(id);
  }, [copied]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      // Clipboard access can be refused (insecure context, permissions); the value is still selectable.
    }
  };

  return (
    <Tooltip title={copied ? 'Kopyalandı' : label}>
      <IconButton size="small" onClick={copy} aria-label={label}>
        {copied ? <CheckIcon fontSize="inherit" color="success" /> : <CopyIcon fontSize="inherit" />}
      </IconButton>
    </Tooltip>
  );
}

export function ConfirmDialog({ open, title, description, confirmLabel, tone = 'error', busy, onClose, onConfirm }) {
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <DialogContentText variant="body2">{description}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button variant="contained" color={tone} disabled={busy} onClick={onConfirm}>{confirmLabel}</Button>
      </DialogActions>
    </Dialog>
  );
}

export function ReasonDialog({ open, title, description, confirmLabel, onClose, onConfirm, busy }) {
  const [reason, setReason] = useState('');
  useEffect(() => {
    if (open) setReason('');
  }, [open]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {description && <DialogContentText variant="body2" sx={{ mb: 2 }}>{description}</DialogContentText>}
        <TextField
          autoFocus fullWidth multiline minRows={3} label="Gerekçe" value={reason}
          onChange={(e) => setReason(e.target.value)}
          helperText={`${reason.length}/500 · Denetim kaydına ve işlem geçmişine eklenir.`}
          slotProps={{ htmlInput: { maxLength: 500 } }}
          sx={{ mt: 0.5 }}
        />
      </DialogContent>
      <DialogActions>
        <Button variant="outlined" color="inherit" onClick={onClose}>Vazgeç</Button>
        <Button variant="contained" color="error" disabled={!reason.trim() || busy} onClick={() => onConfirm(reason.trim())}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
