import { NavLink } from 'react-router-dom';
import { LayoutDashboard, BookOpen, Sparkles, Briefcase, Settings } from 'lucide-react';
import { cn } from '@/lib/utils';

interface NavItem {
  to: string;
  label: string;
  icon: typeof LayoutDashboard;
  end?: boolean;
}

const NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/knowledge', label: 'Knowledge base', icon: BookOpen },
  { to: '/tailor', label: 'Tailor', icon: Sparkles },
  { to: '/applications', label: 'Applications', icon: Briefcase },
  { to: '/settings', label: 'Settings', icon: Settings },
];

export function Sidebar() {
  return (
    <aside className="flex h-full w-60 shrink-0 flex-col border-r border-[var(--border)] bg-[var(--bg-elevated)]">
      <div className="flex items-center gap-2 px-5 py-5">
        <div className="flex h-8 w-8 items-center justify-center rounded-[var(--radius-sm)] bg-[var(--accent)] text-sm font-bold text-white">
          RF
        </div>
        <span className="text-sm font-semibold tracking-tight text-[var(--text)]">ResumeForge</span>
      </div>
      <nav className="flex flex-1 flex-col gap-1 px-3" aria-label="Primary">
        {NAV_ITEMS.map(({ to, label, icon: Icon, end }) => (
          <NavLink
            key={to}
            to={to}
            end={end}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-2.5 rounded-[var(--radius-sm)] px-3 py-2 text-sm font-medium transition-colors',
                'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-1',
                isActive
                  ? 'bg-[var(--accent-soft)] text-[var(--accent-text)]'
                  : 'text-[var(--text-muted)] hover:bg-[var(--bg-inset)] hover:text-[var(--text)]',
              )
            }
          >
            <Icon className="h-4 w-4" aria-hidden="true" />
            {label}
          </NavLink>
        ))}
      </nav>
      <div className="border-t border-[var(--border)] px-5 py-4 text-xs text-[var(--text-faint)]">
        <p>Local-first resume tailoring</p>
      </div>
    </aside>
  );
}
