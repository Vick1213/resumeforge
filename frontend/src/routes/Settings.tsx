import { useEffect, useState } from 'react';
import { KeyRound, ShieldAlert } from 'lucide-react';
import { useProfile, useUpdateBasics } from '@/api/queries';
import type { ResumeBasics } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { Input } from '@/components/ui/Input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';

const MODEL_OPTIONS = [
  { value: 'claude-sonnet-5', label: 'Claude Sonnet 5 (default)' },
  { value: 'claude-opus-5', label: 'Claude Opus 5' },
  { value: 'claude-haiku-5', label: 'Claude Haiku 5' },
];

const EMPTY_BASICS: ResumeBasics = { fullName: '' };

const BASICS_FIELDS: { key: keyof ResumeBasics; label: string; type: string }[] = [
  { key: 'fullName', label: 'Full name', type: 'text' },
  { key: 'headline', label: 'Headline', type: 'text' },
  { key: 'email', label: 'Email', type: 'email' },
  { key: 'phone', label: 'Phone', type: 'tel' },
  { key: 'location', label: 'Location', type: 'text' },
  { key: 'website', label: 'Website', type: 'url' },
  { key: 'linkedIn', label: 'LinkedIn', type: 'url' },
  { key: 'gitHub', label: 'GitHub', type: 'url' },
];

export default function Settings() {
  const profile = useProfile();
  const updateBasics = useUpdateBasics();

  const [basics, setBasics] = useState<ResumeBasics>(EMPTY_BASICS);
  const [model, setModel] = useState('claude-sonnet-5');
  const [savedRecently, setSavedRecently] = useState(false);

  useEffect(() => {
    if (profile.data) {
      setBasics(profile.data.basics);
      setModel(profile.data.model);
    }
  }, [profile.data]);

  function updateField(key: keyof ResumeBasics, value: string): void {
    setBasics((prev) => ({ ...prev, [key]: value }));
    setSavedRecently(false);
  }

  function handleSave(): void {
    updateBasics.mutate(basics, {
      onSuccess: () => {
        setSavedRecently(true);
      },
    });
  }

  return (
    <div className="flex flex-col gap-8">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Settings</h1>
        <p className="mt-1 text-sm text-[var(--text-muted)]">Profile basics, model preference, and API key status.</p>
      </header>

      {profile.isLoading && (
        <div className="flex flex-col gap-4">
          <Skeleton className="h-72" />
          <Skeleton className="h-40" />
        </div>
      )}

      {profile.isError && <ErrorState description="Could not load your profile." onRetry={() => void profile.refetch()} />}

      {profile.data && (
        <>
          <Card>
            <CardHeader>
              <CardTitle>Profile basics</CardTitle>
              <CardDescription>Used as the header of every generated resume.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="grid gap-4 sm:grid-cols-2">
                {BASICS_FIELDS.map((field) => (
                  <div key={field.key} className="flex flex-col gap-1.5">
                    <label htmlFor={`basics-${field.key}`} className="text-xs font-medium text-[var(--text-muted)]">
                      {field.label}
                    </label>
                    <Input
                      id={`basics-${field.key}`}
                      type={field.type}
                      value={basics[field.key] ?? ''}
                      onChange={(event) => updateField(field.key, event.target.value)}
                      required={field.key === 'fullName'}
                    />
                  </div>
                ))}
              </div>
              <div className="mt-5 flex items-center gap-3">
                <Button onClick={handleSave} disabled={updateBasics.isPending || basics.fullName.trim().length === 0}>
                  {updateBasics.isPending ? 'Saving…' : 'Save changes'}
                </Button>
                {savedRecently && <span className="text-xs text-[var(--success)]">Saved</span>}
                {updateBasics.isError && <span className="text-xs text-[var(--danger)]">Could not save changes</span>}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Model preference</CardTitle>
              <CardDescription>Which Anthropic model proposes tailoring commands.</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="flex flex-col gap-1.5 sm:max-w-xs">
                <label htmlFor="model-select" className="text-xs font-medium text-[var(--text-muted)]">
                  Model
                </label>
                <Select value={model} onValueChange={setModel}>
                  <SelectTrigger id="model-select">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MODEL_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-xs text-[var(--text-faint)]">
                  Stored locally until the API exposes a model preference endpoint.
                </p>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>API key</CardTitle>
              <CardDescription>The key itself is never sent to or displayed by this app.</CardDescription>
            </CardHeader>
            <CardContent>
              {profile.data.hasApiKey ? (
                <div className="flex items-start gap-3">
                  <KeyRound className="mt-0.5 h-5 w-5 text-[var(--success)]" aria-hidden="true" />
                  <div>
                    <Badge tone="success">Configured</Badge>
                    <p className="mt-1.5 text-sm text-[var(--text-muted)]">
                      ANTHROPIC_API_KEY is set on the server. Tailoring runs use the live model.
                    </p>
                  </div>
                </div>
              ) : (
                <div className="flex items-start gap-3">
                  <ShieldAlert className="mt-0.5 h-5 w-5 text-[var(--warning)]" aria-hidden="true" />
                  <div>
                    <Badge tone="warning">Not configured</Badge>
                    <p className="mt-1.5 text-sm text-[var(--text-muted)]">
                      No ANTHROPIC_API_KEY is set on the server, so tailoring runs fall back to the deterministic
                      heuristic model. Set the environment variable and restart the API to enable model-backed
                      rewrites.
                    </p>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}
