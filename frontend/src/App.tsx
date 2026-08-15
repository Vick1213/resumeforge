import type { ReactNode } from 'react';
import { Route, Routes } from 'react-router-dom';
import { AppShell } from '@/components/layout/AppShell';
import { RouteErrorBoundary } from '@/components/layout/ErrorBoundary';
import Dashboard from '@/routes/Dashboard';
import Knowledge from '@/routes/Knowledge';
import KnowledgeImport from '@/routes/KnowledgeImport';
import Tailor from '@/routes/Tailor';
import Applications from '@/routes/Applications';
import Settings from '@/routes/Settings';

function guarded(element: ReactNode): ReactNode {
  return <RouteErrorBoundary>{element}</RouteErrorBoundary>;
}

export default function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={guarded(<Dashboard />)} />
        <Route path="/knowledge" element={guarded(<Knowledge />)} />
        <Route path="/knowledge/import" element={guarded(<KnowledgeImport />)} />
        <Route path="/tailor" element={guarded(<Tailor />)} />
        <Route path="/applications" element={guarded(<Applications />)} />
        <Route path="/settings" element={guarded(<Settings />)} />
      </Routes>
    </AppShell>
  );
}
