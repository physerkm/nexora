import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { theme } from '../theme';
import { AuthProvider, useAuth } from '../auth/AuthContext';
import { NotifyProvider } from '../components/Notify';
import App from '../App';
import { signInAs } from './server';

function WhenSignedIn({ children }) {
  const { user } = useAuth();
  return user ? children : null;
}

/** Renders a single component for `user` (signed in through the mocked refresh endpoint). */
export function renderSignedIn(ui, user) {
  signInAs(user);
  return render(
    <ThemeProvider theme={theme}>
      <NotifyProvider>
        <MemoryRouter>
          <AuthProvider>
            <WhenSignedIn>{ui}</WhenSignedIn>
          </AuthProvider>
        </MemoryRouter>
      </NotifyProvider>
    </ThemeProvider>,
  );
}

/** Renders the whole app (same providers as main.jsx) at `path`, with an in-memory router. */
export function renderApp(path = '/') {
  return render(
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <NotifyProvider>
        <MemoryRouter initialEntries={[path]}>
          <AuthProvider>
            <App />
          </AuthProvider>
        </MemoryRouter>
      </NotifyProvider>
    </ThemeProvider>,
  );
}
