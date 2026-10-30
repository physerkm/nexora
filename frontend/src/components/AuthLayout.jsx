import { Box, Stack, Typography } from '@mui/material';
import ShieldIcon from '@mui/icons-material/ShieldOutlined';
import RuleIcon from '@mui/icons-material/FactCheckOutlined';
import HistoryIcon from '@mui/icons-material/HistoryEduOutlined';
import BoltIcon from '@mui/icons-material/BoltOutlined';
import { Logo } from './Brand';
import { useDocumentTitle } from './Common';

const FEATURES = [
  { icon: <ShieldIcon />, title: 'Şirketler arası tam izolasyon', text: 'Her şirketin verisi diğer şirketlerden tamamen ayrı tutulur.' },
  { icon: <RuleIcon />, title: 'Dört göz ilkesiyle onay', text: 'İşlemi oluşturan kişi kendi işlemini onaylayamaz.' },
  { icon: <HistoryIcon />, title: 'Değiştirilemez denetim kaydı', text: 'Her değişiklik kim, ne zaman ve nereden bilgisiyle saklanır.' },
  { icon: <BoltIcon />, title: 'Anlık raporlama', text: 'Hacim, ücret geliri ve şube performansı tek ekranda.' },
];

function Showcase() {
  return (
    <Box
      component="aside"
      sx={{
        display: { xs: 'none', md: 'flex' },
        flexDirection: 'column',
        justifyContent: 'space-between',
        position: 'relative',
        overflow: 'hidden',
        p: { md: 5, lg: 7 },
        color: '#fafafa',
        bgcolor: '#0b0b12',
        // A faint grid and a single soft glow: depth without decoration competing with the copy.
        backgroundImage: [
          'radial-gradient(60% 50% at 20% 0%, rgba(99,102,241,0.35) 0%, rgba(99,102,241,0) 70%)',
          'linear-gradient(rgba(255,255,255,0.04) 1px, transparent 1px)',
          'linear-gradient(90deg, rgba(255,255,255,0.04) 1px, transparent 1px)',
        ].join(','),
        backgroundSize: '100% 100%, 32px 32px, 32px 32px',
      }}
    >
      <Logo size={30} color="#fafafa" />
      <Box sx={{ maxWidth: 460 }}>
        <Typography sx={{ fontSize: { md: 30, lg: 36 }, fontWeight: 650, lineHeight: 1.2, letterSpacing: '-0.025em' }}>
          Ödeme operasyonlarınız, tek ve güvenli bir panelde.
        </Typography>
        <Typography sx={{ mt: 2, color: 'rgba(250,250,250,0.65)', fontSize: 16, lineHeight: 1.6 }}>
          Çok şirketli işlem yönetimi, onay akışları ve raporlama; her adımı kayıt altında.
        </Typography>
        <Stack component="ul" spacing={2.5} sx={{ listStyle: 'none', p: 0, m: 0, mt: 5 }}>
          {FEATURES.map((f) => (
            <Stack component="li" key={f.title} direction="row" spacing={2}>
              <Box
                sx={{
                  width: 36, height: 36, flexShrink: 0, borderRadius: 2, display: 'grid', placeItems: 'center',
                  color: '#a5b4fc', bgcolor: 'rgba(129,140,248,0.12)', border: '1px solid rgba(129,140,248,0.25)',
                  '& svg': { fontSize: 20 },
                }}
              >
                {f.icon}
              </Box>
              <Box>
                <Typography sx={{ fontWeight: 600, fontSize: 15 }}>{f.title}</Typography>
                <Typography sx={{ color: 'rgba(250,250,250,0.6)', fontSize: 14 }}>{f.text}</Typography>
              </Box>
            </Stack>
          ))}
        </Stack>
      </Box>
      <Typography sx={{ color: 'rgba(250,250,250,0.45)', fontSize: 13 }}>© {new Date().getFullYear()} Nexora</Typography>
    </Box>
  );
}

/** Split-screen frame shared by sign-in and password recovery. */
export default function AuthLayout({ title, subtitle, onSubmit, children, footer }) {
  useDocumentTitle(title);
  return (
    <Box sx={{ minHeight: '100dvh', display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr', xl: '5fr 6fr' } }}>
      <Showcase />
      <Box component="main" sx={{ display: 'flex', flexDirection: 'column', bgcolor: 'background.paper', px: { xs: 2.5, sm: 6 }, py: { xs: 4, sm: 6 } }}>
        <Box sx={{ display: { md: 'none' } }}><Logo /></Box>
        <Box sx={{ flex: 1, display: 'grid', placeItems: 'center', py: 4 }}>
          <Box component={onSubmit ? 'form' : 'div'} onSubmit={onSubmit} sx={{ width: '100%', maxWidth: 400 }}>
            <Typography variant="h4" component="h1">{title}</Typography>
            {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>{subtitle}</Typography>}
            <Stack spacing={2.5} sx={{ mt: 4 }}>{children}</Stack>
            {footer && <Box sx={{ mt: 4 }}>{footer}</Box>}
          </Box>
        </Box>
      </Box>
    </Box>
  );
}
