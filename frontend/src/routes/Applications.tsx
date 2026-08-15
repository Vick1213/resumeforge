import { useState } from 'react';
import { Plus } from 'lucide-react';
import { useApplications, useCreateApplication, useUpdateApplication } from '@/api/queries';
import type { ApplicationDto, ApplicationStatus } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { formatRelativeTime } from '@/lib/time';

const COLUMNS: { status: ApplicationStatus; label: string }[] = [
  { status: 'saved', label: 'Saved' },
  { status: 'applied', label: 'Applied' },
  { status: 'screening', label: 'Screening' },
  { status: 'interview', label: 'Interview' },
  { status: 'offer', label: 'Offer' },
  { status: 'rejected', label: 'Rejected' },
];

interface ApplicationCardProps {
  application: ApplicationDto;
  onStatusChange: (status: ApplicationStatus) => void;
}

function ApplicationCard({ application, onStatusChange }: ApplicationCardProps) {
  return (
    <Card>
      <div className="flex flex-col gap-2 p-3.5">
        <p className="text-sm font-medium text-[var(--text)]">{application.title}</p>
        <p className="text-xs text-[var(--text-muted)]">{application.company}</p>
        {application.location && <p className="text-xs text-[var(--text-faint)]">{application.location}</p>}
        {application.coverageScore !== undefined && (
          <Badge tone={application.coverageScore >= 0.75 ? 'success' : 'warning'} className="w-fit">
            {Math.round(application.coverageScore * 100)}% coverage
          </Badge>
        )}
        <Select value={application.status} onValueChange={(value) => onStatusChange(value as ApplicationStatus)}>
          <SelectTrigger aria-label={`Status for ${application.title}`} className="h-8 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {COLUMNS.map((column) => (
              <SelectItem key={column.status} value={column.status}>
                {column.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-[10px] text-[var(--text-faint)]">Updated {formatRelativeTime(application.updatedAt)}</p>
      </div>
    </Card>
  );
}

export default function Applications() {
  const applications = useApplications();
  const updateApplication = useUpdateApplication();
  const createApplication = useCreateApplication();

  const [dialogOpen, setDialogOpen] = useState(false);
  const [company, setCompany] = useState('');
  const [title, setTitle] = useState('');
  const [jobUrl, setJobUrl] = useState('');
  const [location, setLocation] = useState('');

  function resetForm(): void {
    setCompany('');
    setTitle('');
    setJobUrl('');
    setLocation('');
  }

  function handleCreate(): void {
    createApplication.mutate(
      {
        jobId: `job-manual-${Date.now()}`,
        company,
        title,
        jobUrl: jobUrl.trim() || undefined,
        location: location.trim() || undefined,
        status: 'saved',
      },
      {
        onSuccess: () => {
          setDialogOpen(false);
          resetForm();
        },
      },
    );
  }

  const data = applications.data ?? [];

  return (
    <div className="flex flex-col gap-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Applications</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Track every tailored resume from saved through offer.</p>
        </div>
        <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
          <DialogTrigger asChild>
            <Button size="sm">
              <Plus className="h-4 w-4" aria-hidden="true" />
              New application
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>New application</DialogTitle>
              <DialogDescription>Track a role you are applying to, even without a tailored resume yet.</DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-3">
              <div className="flex flex-col gap-1.5">
                <label htmlFor="app-company" className="text-xs font-medium text-[var(--text-muted)]">
                  Company
                </label>
                <Input id="app-company" value={company} onChange={(event) => setCompany(event.target.value)} required />
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="app-title" className="text-xs font-medium text-[var(--text-muted)]">
                  Title
                </label>
                <Input id="app-title" value={title} onChange={(event) => setTitle(event.target.value)} required />
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="app-location" className="text-xs font-medium text-[var(--text-muted)]">
                  Location
                </label>
                <Input id="app-location" value={location} onChange={(event) => setLocation(event.target.value)} />
              </div>
              <div className="flex flex-col gap-1.5">
                <label htmlFor="app-url" className="text-xs font-medium text-[var(--text-muted)]">
                  Job URL
                </label>
                <Input id="app-url" type="url" value={jobUrl} onChange={(event) => setJobUrl(event.target.value)} />
              </div>
            </div>
            <DialogFooter>
              <Button variant="ghost" onClick={() => setDialogOpen(false)}>
                Cancel
              </Button>
              <Button
                disabled={company.trim().length === 0 || title.trim().length === 0 || createApplication.isPending}
                onClick={handleCreate}
              >
                {createApplication.isPending ? 'Adding…' : 'Add application'}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </header>

      {applications.isLoading && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-64" />
          ))}
        </div>
      )}

      {applications.isError && (
        <ErrorState description="Could not load applications." onRetry={() => void applications.refetch()} />
      )}

      {applications.data &&
        (data.length === 0 ? (
          <EmptyState
            title="No applications yet"
            description="Save a tailored resume from the Tailor page, or add one manually."
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
            {COLUMNS.map((column) => {
              const items = data.filter((application) => application.status === column.status);
              return (
                <div key={column.status} className="flex flex-col gap-3">
                  <div className="flex items-center justify-between">
                    <h2 className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">{column.label}</h2>
                    <Badge tone="neutral">{items.length}</Badge>
                  </div>
                  <div className="flex flex-col gap-3">
                    {items.map((application) => (
                      <ApplicationCard
                        key={application.id}
                        application={application}
                        onStatusChange={(status) => updateApplication.mutate({ id: application.id, request: { status } })}
                      />
                    ))}
                    {items.length === 0 && <p className="text-xs text-[var(--text-faint)]">No applications</p>}
                  </div>
                </div>
              );
            })}
          </div>
        ))}
    </div>
  );
}
