import { useState } from 'react';
import { IconButton, InputAdornment, TextField, Tooltip } from '@mui/material';
import ShowIcon from '@mui/icons-material/VisibilityOutlined';
import HideIcon from '@mui/icons-material/VisibilityOffOutlined';

/** A password input with a show/hide toggle. */
export default function PasswordField(props) {
  const [visible, setVisible] = useState(false);
  // Named without the word "Parola" so label lookups for the field itself stay unambiguous.
  const toggleLabel = visible ? 'Karakterleri gizle' : 'Karakterleri göster';

  return (
    <TextField
      {...props}
      type={visible ? 'text' : 'password'}
      slotProps={{
        ...props.slotProps,
        input: {
          endAdornment: (
            <InputAdornment position="end">
              <Tooltip title={toggleLabel}>
                <IconButton edge="end" size="small" aria-label={toggleLabel} onClick={() => setVisible((v) => !v)}>
                  {visible ? <HideIcon fontSize="small" /> : <ShowIcon fontSize="small" />}
                </IconButton>
              </Tooltip>
            </InputAdornment>
          ),
        },
      }}
    />
  );
}
