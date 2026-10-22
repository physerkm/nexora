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
        onClose={() => setMessage(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        {message ? (
          <Alert severity={message.severity} variant="filled" onClose={() => setMessage(null)}>
            {message.text}
          </Alert>
        ) : undefined}
      </Snackbar>
    </NotifyContext.Provider>
  );
}

export const useNotify = () => useContext(NotifyContext);
