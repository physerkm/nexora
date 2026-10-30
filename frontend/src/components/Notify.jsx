import { createContext, useCallback, useContext, useState } from 'react';
import { Alert, Snackbar } from '@mui/material';

const NotifyContext = createContext(() => {});

export function NotifyProvider({ children }) {
  const [message, setMessage] = useState(null);

  const notify = useCallback((text, severity = 'success') => setMessage({ text, severity, key: Date.now() }), []);

  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar
        key={message?.key}
        open={!!message}
        autoHideDuration={5000}
        onClose={(_, reason) => reason !== 'clickaway' && setMessage(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        {message ? (
          <Alert
            severity={message.severity}
            onClose={() => setMessage(null)}
            sx={{
              minWidth: 300,
              alignItems: 'center',
              bgcolor: 'background.paper',
              boxShadow: '0 12px 32px -12px rgba(16,24,40,0.3), 0 4px 8px -4px rgba(16,24,40,0.1)',
            }}
          >
            {message.text}
          </Alert>
        ) : undefined}
      </Snackbar>
    </NotifyContext.Provider>
  );
}

export const useNotify = () => useContext(NotifyContext);
