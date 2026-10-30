import { useId } from 'react';
import { Box, Stack, Typography } from '@mui/material';

export function BrandMark({ size = 28 }) {
  const gradient = useId();
  return (
    <Box component="svg" viewBox="0 0 32 32" aria-hidden="true" sx={{ width: size, height: size, flexShrink: 0, display: 'block' }}>
      <defs>
        <linearGradient id={gradient} x1="0" y1="0" x2="32" y2="32" gradientUnits="userSpaceOnUse">
          <stop stopColor="#6366f1" />
          <stop offset="1" stopColor="#4338ca" />
        </linearGradient>
      </defs>
      <rect width="32" height="32" rx="8" fill={`url(#${gradient})`} />
      <path d="M10 22.5V9.5h2.6l6.8 8.3V9.5H22v13h-2.6l-6.8-8.3v8.3z" fill="#fff" />
    </Box>
  );
}

export function Logo({ size = 28, color }) {
  return (
    <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
      <BrandMark size={size} />
      <Typography component="span" sx={{ fontWeight: 650, fontSize: size * 0.62, letterSpacing: '-0.02em', color }}>
        Nexora
      </Typography>
    </Stack>
  );
}
