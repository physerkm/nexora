import { createTheme } from '@mui/material/styles';
import { trTR as coreTrTR } from '@mui/material/locale';
import { trTR as gridTrTR } from '@mui/x-data-grid/locales';

export const FONT_SANS = '"Inter Variable", "Inter", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';
export const FONT_MONO = 'ui-monospace, "SFMono-Regular", "Cascadia Code", Menlo, Consolas, monospace';

/** `rgba()` of a palette colour through its CSS-variable channel, so it follows the active colour scheme. */
export const tint = (theme, color, opacity) => `rgba(${theme.vars.palette[color].mainChannel} / ${opacity})`;

const SEVERITIES = ['success', 'info', 'warning', 'error'];

export const theme = createTheme(
  {
    // 'class' lets the theme button switch modes; the default ('media') only follows the OS setting.
    cssVariables: { colorSchemeSelector: 'class' },
    colorSchemes: {
      light: {
        palette: {
          primary: { main: '#4f46e5', light: '#818cf8', dark: '#4338ca', contrastText: '#ffffff' },
          secondary: { main: '#52525b', light: '#a1a1aa', dark: '#27272a', contrastText: '#ffffff' },
          success: { main: '#059669', light: '#34d399', dark: '#047857' },
          warning: { main: '#d97706', light: '#fbbf24', dark: '#b45309' },
          error: { main: '#dc2626', light: '#f87171', dark: '#b91c1c' },
          info: { main: '#2563eb', light: '#60a5fa', dark: '#1d4ed8' },
          text: { primary: '#18181b', secondary: '#52525b', disabled: '#a1a1aa' },
          background: { default: '#fafafa', paper: '#ffffff' },
          divider: '#e4e4e7',
          action: { hover: 'rgba(24, 24, 27, 0.04)', selected: 'rgba(79, 70, 229, 0.08)' },
        },
      },
      dark: {
        palette: {
          primary: { main: '#818cf8', light: '#a5b4fc', dark: '#6366f1', contrastText: '#09090b' },
          secondary: { main: '#a1a1aa', light: '#d4d4d8', dark: '#71717a', contrastText: '#09090b' },
          success: { main: '#34d399', light: '#6ee7b7', dark: '#10b981' },
          warning: { main: '#fbbf24', light: '#fcd34d', dark: '#f59e0b' },
          error: { main: '#f87171', light: '#fca5a5', dark: '#ef4444' },
          info: { main: '#60a5fa', light: '#93c5fd', dark: '#3b82f6' },
          text: { primary: '#fafafa', secondary: '#a1a1aa', disabled: '#52525b' },
          background: { default: '#09090b', paper: '#111113' },
          divider: '#27272a',
          action: { hover: 'rgba(250, 250, 250, 0.05)', selected: 'rgba(129, 140, 248, 0.14)' },
        },
      },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: FONT_SANS,
      h4: { fontSize: '1.5rem', fontWeight: 600, lineHeight: 1.3, letterSpacing: '-0.02em' },
      h5: { fontSize: '1.25rem', fontWeight: 600, lineHeight: 1.35, letterSpacing: '-0.015em' },
      h6: { fontSize: '1rem', fontWeight: 600, lineHeight: 1.4, letterSpacing: '-0.01em' },
      subtitle1: { fontSize: '0.9375rem', fontWeight: 600, lineHeight: 1.5 },
      subtitle2: { fontSize: '0.8125rem', fontWeight: 600, lineHeight: 1.5 },
      body1: { fontSize: '0.9375rem', lineHeight: 1.6 },
      body2: { fontSize: '0.875rem', lineHeight: 1.55 },
      caption: { fontSize: '0.75rem', lineHeight: 1.5 },
      overline: { fontSize: '0.6875rem', fontWeight: 600, lineHeight: 1.5, letterSpacing: '0.06em' },
      button: { textTransform: 'none', fontWeight: 500, letterSpacing: 0 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: (theme) => ({
          html: { WebkitFontSmoothing: 'antialiased', MozOsxFontSmoothing: 'grayscale', textRendering: 'optimizeLegibility' },
          // Inter alternates: open digits and a distinguishable l/I, which matters on a screen full of references and amounts.
          body: { fontFeatureSettings: '"cv11", "ss01"' },
          '::selection': { backgroundColor: tint(theme, 'primary', 0.2) },
          '@media (prefers-reduced-motion: reduce)': {
            '*, *::before, *::after': { animationDuration: '0.01ms !important', transitionDuration: '0.01ms !important', scrollBehavior: 'auto !important' },
          },
        }),
      },
      MuiButtonBase: {
        defaultProps: { disableRipple: true },
        styleOverrides: {
          root: ({ theme }) => ({
            '&.Mui-focusVisible': { outline: `2px solid ${theme.vars.palette.primary.main}`, outlineOffset: 2 },
          }),
        },
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: ({ theme }) => ({
            borderRadius: 8,
            minHeight: 36,
            paddingInline: 14,
            whiteSpace: 'nowrap',
            transition: 'background-color 120ms, border-color 120ms, color 120ms, box-shadow 120ms',
            variants: [
              {
                props: { variant: 'contained' },
                style: { boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.12), 0 1px 2px rgba(16,24,40,0.12)' },
              },
              {
                // The neutral "secondary" button: quiet border, primary ink.
                props: { variant: 'outlined', color: 'inherit' },
                style: {
                  borderColor: theme.vars.palette.divider,
                  backgroundColor: theme.vars.palette.background.paper,
                  boxShadow: '0 1px 2px rgba(16,24,40,0.05)',
                  '&:hover': { borderColor: theme.vars.palette.text.disabled, backgroundColor: theme.vars.palette.action.hover },
                },
              },
            ],
          }),
          sizeSmall: { minHeight: 30, paddingInline: 10, fontSize: '0.8125rem' },
          sizeLarge: { minHeight: 42, fontSize: '0.9375rem' },
          startIcon: { marginRight: 6 },
        },
      },
      MuiIconButton: {
        styleOverrides: {
          root: ({ theme }) => ({ borderRadius: 8, color: theme.vars.palette.text.secondary }),
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: ({ theme }) => ({
            backgroundColor: theme.vars.palette.background.paper,
            borderRadius: 8,
            transition: 'box-shadow 120ms',
            '& .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.divider },
            '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: theme.vars.palette.text.disabled },
            '&.Mui-focused': { boxShadow: `0 0 0 3px ${tint(theme, 'primary', 0.18)}` },
            '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderWidth: 1, borderColor: theme.vars.palette.primary.main },
            '&.Mui-error.Mui-focused': { boxShadow: `0 0 0 3px ${tint(theme, 'error', 0.18)}` },
            '&.Mui-disabled': { backgroundColor: theme.vars.palette.action.hover },
          }),
          input: ({ theme }) => ({ '&::placeholder': { color: theme.vars.palette.text.secondary, opacity: 0.75 } }),
        },
      },
      MuiFormHelperText: { styleOverrides: { root: { marginLeft: 2, marginRight: 0 } } },
      MuiPaper: {
        defaultProps: { elevation: 0 },
        styleOverrides: { root: { backgroundImage: 'none' } },
      },
      MuiCard: {
        styleOverrides: {
          root: ({ theme }) => ({
            borderRadius: 12,
            border: `1px solid ${theme.vars.palette.divider}`,
            boxShadow: '0 1px 2px rgba(16,24,40,0.04)',
          }),
        },
      },
      MuiCardContent: {
        styleOverrides: { root: { padding: 20, '&:last-child': { paddingBottom: 20 } } },
      },
      MuiPopover: {
        styleOverrides: {
          paper: ({ theme }) => ({
            marginTop: 4,
            borderRadius: 10,
            border: `1px solid ${theme.vars.palette.divider}`,
            boxShadow: '0 12px 32px -12px rgba(16,24,40,0.24), 0 4px 8px -4px rgba(16,24,40,0.08)',
          }),
        },
      },
      MuiMenu: { styleOverrides: { list: { padding: 4 } } },
      MuiMenuItem: {
        styleOverrides: {
          root: { borderRadius: 6, minHeight: 34, fontSize: '0.875rem', '& + .MuiDivider-root': { marginBlock: 4 } },
        },
      },
      MuiListItemButton: { styleOverrides: { root: { borderRadius: 8 } } },
      MuiDialog: {
        styleOverrides: {
          paper: ({ theme }) => ({
            borderRadius: 14,
            border: `1px solid ${theme.vars.palette.divider}`,
            boxShadow: '0 24px 48px -12px rgba(16,24,40,0.28)',
          }),
        },
      },
      MuiDialogTitle: { styleOverrides: { root: { fontSize: '1.0625rem', fontWeight: 600, letterSpacing: '-0.01em', padding: '20px 24px 12px' } } },
      MuiDialogContent: { styleOverrides: { root: { padding: '4px 24px 8px' } } },
      MuiDialogActions: { styleOverrides: { root: { padding: '16px 24px 20px', gap: 8, '& > :not(style) ~ :not(style)': { marginLeft: 0 } } } },
      MuiBackdrop: {
        styleOverrides: {
          root: { '&:not(.MuiBackdrop-invisible)': { backgroundColor: 'rgba(9, 9, 11, 0.45)', backdropFilter: 'blur(2px)' } },
        },
      },
      MuiTooltip: {
        defaultProps: { enterDelay: 300, disableInteractive: true },
        styleOverrides: {
          tooltip: ({ theme }) => ({
            backgroundColor: '#18181b',
            fontSize: '0.75rem',
            fontWeight: 500,
            borderRadius: 6,
            padding: '6px 8px',
            ...theme.applyStyles('dark', { backgroundColor: '#3f3f46' }),
          }),
        },
      },
      MuiChip: {
        styleOverrides: {
          root: { borderRadius: 6, fontWeight: 500 },
          sizeSmall: { height: 22, fontSize: '0.75rem' },
          labelSmall: { paddingInline: 8 },
        },
      },
      MuiAlert: {
        styleOverrides: {
          root: ({ theme }) => ({
            borderRadius: 10,
            fontSize: '0.875rem',
            variants: SEVERITIES.map((severity) => ({
              props: { variant: 'standard', severity },
              style: {
                color: theme.vars.palette.text.primary,
                backgroundColor: tint(theme, severity, 0.08),
                border: `1px solid ${tint(theme, severity, 0.25)}`,
                '& .MuiAlert-icon': { color: theme.vars.palette[severity].main },
              },
            })),
          }),
        },
      },
      MuiLinearProgress: { styleOverrides: { root: { borderRadius: 4 } } },
      MuiSkeleton: {
        defaultProps: { animation: 'wave' },
        styleOverrides: { rounded: { borderRadius: 8 } },
      },
      MuiToggleButtonGroup: {
        styleOverrides: {
          root: ({ theme }) => ({
            gap: 2,
            padding: 2,
            borderRadius: 8,
            border: `1px solid ${theme.vars.palette.divider}`,
            backgroundColor: theme.vars.palette.action.hover,
          }),
          grouped: { border: 0, margin: 0, borderRadius: '6px !important' },
        },
      },
      MuiToggleButton: {
        styleOverrides: {
          root: ({ theme }) => ({
            padding: '4px 12px',
            fontSize: '0.8125rem',
            fontWeight: 500,
            color: theme.vars.palette.text.secondary,
            '&.Mui-selected, &.Mui-selected:hover': {
              color: theme.vars.palette.text.primary,
              backgroundColor: theme.vars.palette.background.paper,
              boxShadow: '0 1px 2px rgba(16,24,40,0.1)',
            },
          }),
        },
      },
      MuiTableCell: {
        styleOverrides: {
          root: ({ theme }) => ({ borderBottomColor: theme.vars.palette.divider }),
          head: ({ theme }) => ({ fontSize: '0.75rem', fontWeight: 500, color: theme.vars.palette.text.secondary }),
        },
      },
      MuiLink: {
        defaultProps: { underline: 'hover' },
        styleOverrides: { root: { fontWeight: 500 } },
      },
      MuiAvatar: { styleOverrides: { root: { fontSize: '0.8125rem', fontWeight: 600 } } },
      MuiDataGrid: {
        defaultProps: {
          rowHeight: 52,
          columnHeaderHeight: 44,
          disableColumnMenu: true,
          localeText: {
            paginationDisplayedRows: ({ from, to, count, estimated }) => {
              const total = count !== -1 ? count.toLocaleString('tr-TR') : `${(estimated ?? to).toLocaleString('tr-TR')}+`;
              return `${from.toLocaleString('tr-TR')}–${to.toLocaleString('tr-TR')} / ${total}`;
            },
          },
        },
        styleOverrides: {
          root: ({ theme }) => ({
            border: 0,
            fontSize: '0.875rem',
            '--DataGrid-rowBorderColor': theme.vars.palette.divider,
            '--DataGrid-containerBackground': theme.vars.palette.background.paper,
            '& .MuiDataGrid-columnHeaderTitle': { fontSize: '0.75rem', fontWeight: 500, color: theme.vars.palette.text.secondary },
            '& .MuiDataGrid-columnSeparator': { visibility: 'hidden' },
            // Line the first column up with the 20px gutter used by card headers and toolbars.
            '& .MuiDataGrid-columnHeader[aria-colindex="1"], & .MuiDataGrid-cell[aria-colindex="1"]': { paddingLeft: 20 },
            // Mouse focus stays quiet; keyboard focus keeps a visible ring.
            '& .MuiDataGrid-cell:focus, & .MuiDataGrid-cell:focus-within, & .MuiDataGrid-columnHeader:focus, & .MuiDataGrid-columnHeader:focus-within': { outline: 'none' },
            '& .MuiDataGrid-cell:focus-visible, & .MuiDataGrid-columnHeader:focus-visible': {
              outline: `2px solid ${theme.vars.palette.primary.main}`,
              outlineOffset: -2,
            },
            '& .MuiDataGrid-row:hover': { backgroundColor: theme.vars.palette.action.hover },
            '& .MuiDataGrid-footerContainer': { borderTopColor: theme.vars.palette.divider },
            '& .MuiDataGrid-overlay': { backgroundColor: 'transparent' },
          }),
        },
      },
    },
  },
  coreTrTR,
  gridTrTR,
);
