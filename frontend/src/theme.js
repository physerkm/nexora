import { createTheme } from '@mui/material/styles';
import { trTR as coreTrTR } from '@mui/material/locale';
import { trTR as gridTrTR } from '@mui/x-data-grid/locales';

export const theme = createTheme(
  {
    cssVariables: true,
    colorSchemes: {
      light: {
        palette: {
          primary: { main: '#1e3a8a' },
          secondary: { main: '#7c3aed' },
          background: { default: '#f5f7fb', paper: '#ffffff' },
        },
      },
      dark: {
        palette: {
          primary: { main: '#93c5fd' },
          secondary: { main: '#c4b5fd' },
          background: { default: '#0b1220', paper: '#111a2e' },
        },
      },
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily: '"Inter", "Segoe UI", system-ui, sans-serif',
      h5: { fontWeight: 700 },
      h6: { fontWeight: 600 },
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { backgroundImage: 'none' } } },
      MuiCard: { styleOverrides: { root: ({ theme }) => ({ border: `1px solid ${theme.vars.palette.divider}` }) } },
      MuiButton: { defaultProps: { disableElevation: true } },
    },
  },
  coreTrTR,
  gridTrTR,
);
