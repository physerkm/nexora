import { Suspense, useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import {
  AppBar, Avatar, Box, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Menu, MenuItem,
  LinearProgress, Select, Stack, Toolbar, Tooltip, Typography, useColorScheme, useMediaQuery,
} from '@mui/material';
import DashboardIcon from '@mui/icons-material/SpaceDashboardOutlined';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import StoreIcon from '@mui/icons-material/StorefrontOutlined';
import PeopleIcon from '@mui/icons-material/PeopleAltOutlined';
import PercentIcon from '@mui/icons-material/PercentOutlined';
import HistoryIcon from '@mui/icons-material/ManageSearchOutlined';
import BusinessIcon from '@mui/icons-material/ApartmentOutlined';
import SettingsIcon from '@mui/icons-material/TuneOutlined';
import MenuIcon from '@mui/icons-material/MenuRounded';
import DarkIcon from '@mui/icons-material/DarkModeOutlined';
import LightIcon from '@mui/icons-material/LightModeOutlined';
import LogoutIcon from '@mui/icons-material/LogoutRounded';
import UnfoldIcon from '@mui/icons-material/UnfoldMoreRounded';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import { initials, ROLES } from '../utils/format';
import { Logo } from './Brand';

const DRAWER_WIDTH = 252;

const SECTIONS = [
  {
    label: 'Genel',
    items: [
      { to: '/dashboard', label: 'Gösterge paneli', icon: <DashboardIcon />, policy: 'viewReports' },
      { to: '/transactions', label: 'İşlemler', icon: <ReceiptIcon /> },
    ],
  },
  {
    label: 'Yönetim',
    items: [
      { to: '/branches', label: 'Şubeler', icon: <StoreIcon /> },
      { to: '/fees', label: 'Ücret kuralları', icon: <PercentIcon /> },
      { to: '/users', label: 'Kullanıcılar', icon: <PeopleIcon />, policy: 'viewUsers' },
      { to: '/audit', label: 'Denetim kayıtları', icon: <HistoryIcon />, policy: 'viewAudit' },
      { to: '/settings', label: 'Şirket ayarları', icon: <SettingsIcon />, policy: 'manageTenant', tenantOnly: true },
    ],
  },
  {
    label: 'Platform',
    items: [{ to: '/tenants', label: 'Şirketler', icon: <BusinessIcon />, policy: 'platformAdmin' }],
  },
];

const ALL_ITEMS = SECTIONS.flatMap((s) => s.items);

function TenantSwitcher({ fullWidth }) {
  const { tenant, selectTenant } = useAuth();
  const [tenants, setTenants] = useState([]);

  useEffect(() => {
    api('/platform/tenants', { query: { pageSize: 100 } }).then((r) => setTenants(r.items)).catch(() => {});
  }, []);

  return (
    <Select
      size="small"
      value={tenant?.id ?? ''}
      displayEmpty
      fullWidth={fullWidth}
      IconComponent={UnfoldIcon}
      inputProps={{ 'aria-label': 'Şirket' }}
      onChange={(e) => selectTenant(tenants.find((t) => t.id === e.target.value) ?? null)}
      sx={{ minWidth: 240, fontWeight: 500, '& .MuiSelect-icon': { fontSize: 18, color: 'text.secondary' } }}
    >
      <MenuItem value="">Tüm şirketler (salt okunur)</MenuItem>
      {tenants.map((t) => (
        <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
      ))}
    </Select>
  );
}

function SidebarNav({ onNavigate }) {
  const { user, can, isPlatformAdmin, tenant } = useAuth();
  const visible = (item) => (!item.policy || can(item.policy)) && (!item.tenantOnly || !isPlatformAdmin || tenant);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ minHeight: { xs: 60 }, px: { xs: 2.5 } }}>
        <Logo size={26} />
      </Toolbar>
      <Box component="nav" aria-label="Ana menü" sx={{ flex: 1, overflowY: 'auto', px: 1.5, pb: 2 }}>
        {SECTIONS.map((section) => {
          const items = section.items.filter(visible);
          if (items.length === 0) return null;
          return (
            <Box key={section.label} sx={{ mt: 2 }}>
              <Typography variant="overline" component="p" color="text.secondary" sx={{ px: 1.5, mb: 0.5, display: 'block' }}>
                {section.label}
              </Typography>
              <List disablePadding>
                {items.map((item) => (
                  <ListItemButton
                    key={item.to}
                    component={NavLink}
                    to={item.to}
                    onClick={onNavigate}
                    sx={{
                      py: 0.75, px: 1.5, mb: 0.25, color: 'text.secondary',
                      '& .MuiListItemIcon-root': { minWidth: 32, color: 'inherit', '& svg': { fontSize: 20 } },
                      '&:hover': { color: 'text.primary' },
                      '&.active': { color: 'primary.main', bgcolor: 'action.selected' },
                      '&.active .MuiListItemText-primary': { fontWeight: 600 },
                    }}
                  >
                    <ListItemIcon>{item.icon}</ListItemIcon>
                    <ListItemText primary={item.label} slotProps={{ primary: { variant: 'body2', sx: { fontWeight: 500 } } }} />
                  </ListItemButton>
                ))}
              </List>
            </Box>
          );
        })}
      </Box>
      <Divider />
      <Stack direction="row" spacing={1.5} sx={{ p: 2, alignItems: 'center' }}>
        <Avatar variant="rounded" sx={{ width: 32, height: 32, bgcolor: 'action.selected', color: 'primary.main' }}>
          {initials(user.tenantName ?? 'Platform')}
        </Avatar>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" noWrap sx={{ fontWeight: 600 }}>{user.tenantName ?? 'Nexora Platform'}</Typography>
          <Typography variant="caption" color="text.secondary" noWrap component="p">{ROLES[user.roles[0]] ?? user.roles[0]}</Typography>
        </Box>
      </Stack>
    </Box>
  );
}

function ThemeToggle() {
  const { mode, systemMode, setMode } = useColorScheme();
  // "system" resolves to whatever the OS uses; toggle from what the user actually sees.
  const dark = (mode === 'system' ? systemMode : mode) === 'dark';
  return (
    <Tooltip title={dark ? 'Açık temaya geç' : 'Koyu temaya geç'}>
      <IconButton aria-label="Tema" onClick={() => setMode(dark ? 'light' : 'dark')}>
        {dark ? <LightIcon fontSize="small" /> : <DarkIcon fontSize="small" />}
      </IconButton>
    </Tooltip>
  );
}

function AccountMenu() {
  const { user, logout } = useAuth();
  const [anchor, setAnchor] = useState(null);
  const role = ROLES[user.roles[0]] ?? user.roles[0];

  return (
    <>
      <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label="Hesap" aria-haspopup="menu" sx={{ p: 0.5 }}>
        <Avatar sx={{ width: 30, height: 30, bgcolor: 'primary.main', color: 'primary.contrastText' }}>{initials(user.fullName)}</Avatar>
      </IconButton>
      <Menu
        anchorEl={anchor}
        open={!!anchor}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { sx: { minWidth: 240 } } }}
      >
        <Box sx={{ px: 1.5, py: 1 }}>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>{user.fullName}</Typography>
          <Typography variant="caption" color="text.secondary" component="p">{user.email}</Typography>
          <Typography variant="caption" color="text.secondary" component="p">{role}</Typography>
        </Box>
        <Divider />
        <MenuItem onClick={logout}>
          <ListItemIcon><LogoutIcon fontSize="small" /></ListItemIcon>
          Çıkış yap
        </MenuItem>
      </Menu>
    </>
  );
}

export default function Layout() {
  const { user, isPlatformAdmin, scopeKey } = useAuth();
  const desktop = useMediaQuery((t) => t.breakpoints.up('md'));
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();
  const current = ALL_ITEMS.find((item) => location.pathname.startsWith(item.to));

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Box
        component="a"
        href="#main"
        sx={{
          position: 'fixed', left: 12, top: -48, zIndex: (t) => t.zIndex.tooltip, px: 1.5, py: 1, borderRadius: 1,
          bgcolor: 'background.paper', border: 1, borderColor: 'divider', color: 'text.primary', fontSize: 14,
          '&:focus': { top: 12 },
        }}
      >
        İçeriğe geç
      </Box>

      <Drawer
        variant={desktop ? 'permanent' : 'temporary'}
        open={desktop || mobileOpen}
        onClose={() => setMobileOpen(false)}
        ModalProps={{ keepMounted: false }}
        sx={{
          width: DRAWER_WIDTH,
          flexShrink: 0,
          '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box', bgcolor: 'background.default', borderRightColor: 'divider' },
        }}
      >
        <SidebarNav onNavigate={() => setMobileOpen(false)} />
      </Drawer>

      <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <AppBar
          position="sticky"
          color="inherit"
          elevation={0}
          sx={{
            borderBottom: 1,
            borderColor: 'divider',
            bgcolor: 'rgba(var(--mui-palette-background-defaultChannel) / 0.8)',
            backdropFilter: 'saturate(180%) blur(8px)',
          }}
        >
          <Toolbar sx={{ gap: 1.5, minHeight: { xs: 60 }, px: { xs: 2, sm: 3, lg: 4 } }}>
            {!desktop && (
              <IconButton edge="start" onClick={() => setMobileOpen(true)} aria-label="Menü">
                <MenuIcon />
              </IconButton>
            )}
            <Stack direction="row" spacing={1} sx={{ flex: 1, minWidth: 0, alignItems: 'center' }}>
              {isPlatformAdmin ? (
                desktop && <TenantSwitcher />
              ) : (
                <Typography variant="body2" noWrap sx={{ fontWeight: 600 }}>{user.tenantName}</Typography>
              )}
              {/* The page title repeats in the content, so phones keep only the company name. */}
              {current && (desktop || !isPlatformAdmin) && (
                <Stack direction="row" spacing={1} sx={{ display: { xs: 'none', sm: 'flex' }, minWidth: 0 }}>
                  <Typography variant="body2" color="text.disabled" aria-hidden="true">/</Typography>
                  <Typography variant="body2" color="text.secondary" noWrap>{current.label}</Typography>
                </Stack>
              )}
            </Stack>
            <ThemeToggle />
            <AccountMenu />
          </Toolbar>
          {isPlatformAdmin && !desktop && (
            <Box sx={{ px: 2, pb: 1.5 }}><TenantSwitcher fullWidth /></Box>
          )}
        </AppBar>

        <Box
          component="main"
          id="main"
          tabIndex={-1}
          sx={{ outline: 'none', flex: 1, width: '100%', maxWidth: 1440, mx: 'auto', px: { xs: 2, sm: 3, lg: 4 }, py: { xs: 3, lg: 4 } }}
        >
          {/* Keyed by tenant scope: switching tenants remounts pages so nothing from the previous tenant lingers. */}
          <Suspense fallback={<LinearProgress aria-label="Sayfa yükleniyor" sx={{ height: 2 }} />}>
            <Outlet key={scopeKey} />
          </Suspense>
        </Box>
      </Box>
    </Box>
  );
}
