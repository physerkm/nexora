import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import {
  AppBar, Avatar, Box, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText,
  ListSubheader, Menu, MenuItem, Select, Stack, Toolbar, Tooltip, Typography, useColorScheme, useMediaQuery,
} from '@mui/material';
import DashboardIcon from '@mui/icons-material/SpaceDashboardOutlined';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import StoreIcon from '@mui/icons-material/StorefrontOutlined';
import PeopleIcon from '@mui/icons-material/PeopleAltOutlined';
import PercentIcon from '@mui/icons-material/PercentOutlined';
import HistoryIcon from '@mui/icons-material/ManageSearchOutlined';
import BusinessIcon from '@mui/icons-material/ApartmentOutlined';
import SettingsIcon from '@mui/icons-material/TuneOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import DarkIcon from '@mui/icons-material/DarkModeOutlined';
import LightIcon from '@mui/icons-material/LightModeOutlined';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import { ROLES } from '../utils/format';

const DRAWER_WIDTH = 248;

const NAV = [
  { to: '/dashboard', label: 'Gösterge paneli', icon: <DashboardIcon />, policy: 'viewReports' },
  { to: '/transactions', label: 'İşlemler', icon: <ReceiptIcon /> },
  { to: '/branches', label: 'Şubeler', icon: <StoreIcon /> },
  { to: '/users', label: 'Kullanıcılar', icon: <PeopleIcon />, policy: 'viewUsers' },
  { to: '/fees', label: 'Ücret kuralları', icon: <PercentIcon /> },
  { to: '/audit', label: 'Denetim kayıtları', icon: <HistoryIcon />, policy: 'viewAudit' },
  { to: '/settings', label: 'Şirket ayarları', icon: <SettingsIcon />, policy: 'manageTenant', tenantOnly: true },
];

function TenantSwitcher() {
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
      onChange={(e) => selectTenant(tenants.find((t) => t.id === e.target.value) ?? null)}
      sx={{ minWidth: 200, bgcolor: 'background.paper' }}
    >
      <MenuItem value="">Tüm şirketler (salt okunur)</MenuItem>
      {tenants.map((t) => (
        <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
      ))}
    </Select>
  );
}

export default function Layout() {
  const { user, logout, can, isPlatformAdmin, tenant, scopeKey } = useAuth();
  const { mode, setMode } = useColorScheme();
  const desktop = useMediaQuery((t) => t.breakpoints.up('md'));
  const [mobileOpen, setMobileOpen] = useState(false);
  const [menuAnchor, setMenuAnchor] = useState(null);
  const location = useLocation();

  useEffect(() => setMobileOpen(false), [location.pathname]);

  const items = NAV.filter((n) => (!n.policy || can(n.policy)) && (!n.tenantOnly || !isPlatformAdmin || tenant));

  const drawer = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ gap: 1.5 }}>
        <Box sx={{ width: 32, height: 32, borderRadius: 2, bgcolor: 'primary.main', color: 'primary.contrastText', display: 'grid', placeItems: 'center', fontWeight: 800 }}>
          N
        </Box>
        <Typography variant="h6">Nexora</Typography>
      </Toolbar>
      <Divider />
      <List sx={{ px: 1, flex: 1 }}>
        {items.map((item) => (
          <ListItemButton key={item.to} component={NavLink} to={item.to} sx={{ borderRadius: 2, mb: 0.5, '&.active': { bgcolor: 'action.selected' } }}>
            <ListItemIcon sx={{ minWidth: 40 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} />
          </ListItemButton>
        ))}
        {isPlatformAdmin && (
          <>
            <ListSubheader sx={{ bgcolor: 'transparent' }}>Platform</ListSubheader>
            <ListItemButton component={NavLink} to="/tenants" sx={{ borderRadius: 2, '&.active': { bgcolor: 'action.selected' } }}>
              <ListItemIcon sx={{ minWidth: 40 }}><BusinessIcon /></ListItemIcon>
              <ListItemText primary="Şirketler" />
            </ListItemButton>
          </>
        )}
      </List>
      <Typography variant="caption" color="text.secondary" sx={{ p: 2 }}>
        {user.tenantName ?? 'Platform'} · {ROLES[user.roles[0]] ?? user.roles[0]}
      </Typography>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <Drawer
        variant={desktop ? 'permanent' : 'temporary'}
        open={desktop || mobileOpen}
        onClose={() => setMobileOpen(false)}
        sx={{ width: DRAWER_WIDTH, flexShrink: 0, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
      >
        {drawer}
      </Drawer>

      <Box sx={{ flex: 1, minWidth: 0 }}>
        <AppBar position="sticky" color="inherit" sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Toolbar sx={{ gap: 2 }}>
            {!desktop && (
              <IconButton edge="start" onClick={() => setMobileOpen(true)} aria-label="Menü">
                <MenuIcon />
              </IconButton>
            )}
            <Typography variant="subtitle1" sx={{ fontWeight: 600, flex: 1 }} noWrap>
              {isPlatformAdmin ? (tenant ? tenant.name : 'Tüm şirketler') : user.tenantName}
            </Typography>
            {isPlatformAdmin && desktop && <TenantSwitcher />}
            <Tooltip title="Tema">
              <IconButton onClick={() => setMode(mode === 'dark' ? 'light' : 'dark')}>
                {mode === 'dark' ? <LightIcon /> : <DarkIcon />}
              </IconButton>
            </Tooltip>
            <IconButton onClick={(e) => setMenuAnchor(e.currentTarget)} aria-label="Hesap">
              <Avatar sx={{ width: 32, height: 32, bgcolor: 'secondary.main' }}>{user.fullName[0]}</Avatar>
            </IconButton>
            <Menu anchorEl={menuAnchor} open={!!menuAnchor} onClose={() => setMenuAnchor(null)}>
              <Stack sx={{ px: 2, py: 1 }}>
                <Typography variant="subtitle2">{user.fullName}</Typography>
                <Typography variant="caption" color="text.secondary">{user.email}</Typography>
              </Stack>
              <Divider />
              <MenuItem onClick={logout}>Çıkış yap</MenuItem>
            </Menu>
          </Toolbar>
          {isPlatformAdmin && !desktop && (
            <Box sx={{ px: 2, pb: 1.5 }}><TenantSwitcher /></Box>
          )}
        </AppBar>

        <Box component="main" sx={{ p: { xs: 2, md: 3 }, maxWidth: 1400, mx: 'auto' }}>
          {/* Keyed by tenant scope: switching tenants remounts pages so nothing from the previous tenant lingers. */}
          <Outlet key={scopeKey} />
        </Box>
      </Box>
    </Box>
  );
}
