import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { CircleAlert, CircleCheck, Github, Plus, Save, Search } from 'lucide-react';
import { useKnowledge, useKnowledgeItem, useUpsertKnowledge } from '@/api/queries';
import type { KnowledgeItemDto, KnowledgeItemKind } from '@/api/types';
import { parseBullets, splitMarkdown, starterMarkdown, validateFrontmatter } from '@/lib/frontmatter';
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
import { Textarea } from '@/components/ui/Textarea';
import { cn } from '@/lib/utils';

const KIND_LABELS: Record<KnowledgeItemKind, string> = {
  experience: 'Experience',
  project: 'Project',
  education: 'Education',
  certification: 'Certification',
  basics: 'Basics',
};

const KIND_PREFIX: Record<'experience' | 'project' | 'education' | 'certification', string> = {
  experience: 'exp',
  project: 'prj',
  education: 'edu',
  certification: 'cert',
};

const SLUG_PATTERN = /^[a-z0-9-]+$/;

function matchesQuery(item: KnowledgeItemDto, query: string): boolean {
  if (query.length === 0) return true;
  const haystack = [item.title, item.subtitle ?? '', ...item.tech].join(' ').toLowerCase();
  return haystack.includes(query);
}

export default function Knowledge() {
  const knowledgeQuery = useKnowledge();
  const upsertKnowledge = useUpsertKnowledge();

  const [searchQuery, setSearchQuery] = useState('');
  const [kindFilter, setKindFilter] = useState<KnowledgeItemKind | 'all'>('all');
  const [selectedId, setSelectedId] = useState<string | undefined>(undefined);
  const [isNewItem, setIsNewItem] = useState(false);
  const [editorText, setEditorText] = useState('');
  const [isDirty, setIsDirty] = useState(false);

  const [newDialogOpen, setNewDialogOpen] = useState(false);
  const [newKind, setNewKind] = useState<keyof typeof KIND_PREFIX>('experience');
  const [newSlug, setNewSlug] = useState('');

  const detailQuery = useKnowledgeItem(isNewItem ? undefined : selectedId);

  useEffect(() => {
    if (!isNewItem && detailQuery.data) {
      setEditorText(detailQuery.data.rawMarkdown);
      setIsDirty(false);
    }
  }, [isNewItem, detailQuery.data]);

  const filteredItems = useMemo(() => {
    const query = searchQuery.trim().toLowerCase();
    return (knowledgeQuery.data ?? [])
      .filter((item) => kindFilter === 'all' || item.kind === kindFilter)
      .filter((item) => matchesQuery(item, query));
  }, [knowledgeQuery.data, kindFilter, searchQuery]);

  const parsed = useMemo(() => splitMarkdown(editorText), [editorText]);
  const validation = useMemo(() => validateFrontmatter(parsed.frontmatter), [parsed]);
  const bullets = useMemo(() => parseBullets(parsed.body), [parsed]);

  function selectItem(id: string): void {
    setSelectedId(id);
    setIsNewItem(false);
  }

  function openNewDialog(): void {
    setNewKind('experience');
    setNewSlug('');
    setNewDialogOpen(true);
  }

  function createNewItem(): void {
    const id = `${KIND_PREFIX[newKind]}:${newSlug}`;
    setSelectedId(id);
    setIsNewItem(true);
    setEditorText(starterMarkdown(newKind));
    setIsDirty(false);
    setNewDialogOpen(false);
  }

  function handleSave(): void {
    if (!selectedId) return;
    upsertKnowledge.mutate(
      { itemId: selectedId, request: { rawMarkdown: editorText } },
      {
        onSuccess: () => {
          setIsDirty(false);
          setIsNewItem(false);
        },
      },
    );
  }

  const isSlugValid = SLUG_PATTERN.test(newSlug);

  return (
    <div className="flex h-full flex-col gap-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--text)]">Knowledge base</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            The markdown files under profile/ that every tailored resume is built from.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Link
            to="/knowledge/import"
            className="inline-flex h-9 items-center gap-1.5 rounded-[var(--radius-sm)] border border-[var(--border-strong)] px-3 text-sm font-medium text-[var(--text)] hover:bg-[var(--bg-inset)]"
          >
            <Github className="h-4 w-4" aria-hidden="true" />
            Import from GitHub
          </Link>
          <Dialog open={newDialogOpen} onOpenChange={setNewDialogOpen}>
            <DialogTrigger asChild>
              <Button size="sm" onClick={openNewDialog}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                New item
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>New knowledge item</DialogTitle>
                <DialogDescription>Pick a kind and a slug. The slug becomes the entity ID.</DialogDescription>
              </DialogHeader>
              <div className="flex flex-col gap-4">
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="new-kind" className="text-xs font-medium text-[var(--text-muted)]">
                    Kind
                  </label>
                  <Select value={newKind} onValueChange={(value) => setNewKind(value as keyof typeof KIND_PREFIX)}>
                    <SelectTrigger id="new-kind">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(Object.keys(KIND_PREFIX) as (keyof typeof KIND_PREFIX)[]).map((kind) => (
                        <SelectItem key={kind} value={kind}>
                          {KIND_LABELS[kind]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="flex flex-col gap-1.5">
                  <label htmlFor="new-slug" className="text-xs font-medium text-[var(--text-muted)]">
                    Slug
                  </label>
                  <Input
                    id="new-slug"
                    value={newSlug}
                    onChange={(event) => setNewSlug(event.target.value)}
                    placeholder="acme-corp"
                    invalid={newSlug.length > 0 && !isSlugValid}
                  />
                  <p className="text-xs text-[var(--text-faint)]">
                    {KIND_PREFIX[newKind]}:{newSlug || 'slug'} · lowercase letters, numbers, and hyphens only
                  </p>
                </div>
              </div>
              <DialogFooter>
                <Button variant="ghost" onClick={() => setNewDialogOpen(false)}>
                  Cancel
                </Button>
                <Button disabled={!isSlugValid} onClick={createNewItem}>
                  Create
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </header>

      <div className="grid flex-1 grid-cols-1 gap-6 lg:grid-cols-[320px_1fr]">
        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-2">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-faint)]" />
              <Input
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
                placeholder="Search title or tech"
                className="pl-8"
                aria-label="Search knowledge items"
              />
            </div>
            <Select value={kindFilter} onValueChange={(value) => setKindFilter(value as KnowledgeItemKind | 'all')}>
              <SelectTrigger aria-label="Filter by kind">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All kinds</SelectItem>
                {(Object.keys(KIND_LABELS) as KnowledgeItemKind[]).map((kind) => (
                  <SelectItem key={kind} value={kind}>
                    {KIND_LABELS[kind]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {knowledgeQuery.isLoading && (
            <div className="flex flex-col gap-2">
              {Array.from({ length: 4 }, (_, index) => (
                <Skeleton key={index} className="h-16" />
              ))}
            </div>
          )}

          {knowledgeQuery.isError && (
            <ErrorState description="Could not load the knowledge base." onRetry={() => void knowledgeQuery.refetch()} />
          )}

          {knowledgeQuery.data && filteredItems.length === 0 && (
            <EmptyState
              title="No matching items"
              description={
                knowledgeQuery.data.length === 0
                  ? 'Add your first experience, project, or credential to get started.'
                  : 'Try a different search or filter.'
              }
            />
          )}

          <ul className="flex flex-col gap-1.5 overflow-y-auto">
            {filteredItems.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  onClick={() => selectItem(item.id)}
                  className={cn(
                    'flex w-full flex-col gap-1 rounded-[var(--radius-md)] border px-3 py-2.5 text-left transition-colors',
                    'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)]',
                    item.id === selectedId
                      ? 'border-[var(--accent)] bg-[var(--accent-soft)]'
                      : 'border-[var(--border)] bg-[var(--bg-elevated)] hover:bg-[var(--bg-inset)]',
                  )}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-sm font-medium text-[var(--text)]">{item.title}</span>
                    {item.source === 'github' && <Github className="h-3.5 w-3.5 shrink-0 text-[var(--text-faint)]" />}
                  </div>
                  {item.subtitle && <span className="truncate text-xs text-[var(--text-muted)]">{item.subtitle}</span>}
                  <div className="flex flex-wrap gap-1">
                    <Badge tone="neutral">{KIND_LABELS[item.kind]}</Badge>
                    {item.tech.slice(0, 3).map((tech) => (
                      <Badge key={tech} tone="accent">
                        {tech}
                      </Badge>
                    ))}
                  </div>
                </button>
              </li>
            ))}
          </ul>
        </div>

        <div className="flex flex-col gap-4">
          {!selectedId && (
            <EmptyState
              title="Select an item to edit"
              description="Choose a knowledge item from the list, or create a new one."
            />
          )}

          {selectedId && !isNewItem && detailQuery.isLoading && <Skeleton className="h-96" />}

          {selectedId && !isNewItem && detailQuery.isError && (
            <ErrorState description="Could not load this item." onRetry={() => void detailQuery.refetch()} />
          )}

          {selectedId && (isNewItem || detailQuery.data) && (
            <>
              <div className="flex items-center justify-between">
                <div>
                  <p className="font-mono text-xs text-[var(--text-faint)]">{selectedId}</p>
                  {isDirty && <p className="text-xs text-[var(--warning)]">Unsaved changes</p>}
                </div>
                <Button
                  onClick={handleSave}
                  disabled={!validation.isValid || upsertKnowledge.isPending}
                  size="sm"
                >
                  <Save className="h-4 w-4" aria-hidden="true" />
                  {upsertKnowledge.isPending ? 'Saving…' : 'Save'}
                </Button>
              </div>

              <Textarea
                value={editorText}
                onChange={(event) => {
                  setEditorText(event.target.value);
                  setIsDirty(true);
                }}
                rows={16}
                spellCheck={false}
                className="font-mono text-xs leading-relaxed"
                aria-label="Markdown source"
              />

              <div className="grid gap-4 sm:grid-cols-2">
                <Card>
                  <div className="flex flex-col gap-2 p-4">
                    <h3 className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">
                      Frontmatter validation
                    </h3>
                    {validation.issues.length === 0 ? (
                      <p className="flex items-center gap-1.5 text-sm text-[var(--success)]">
                        <CircleCheck className="h-4 w-4" aria-hidden="true" />
                        Looks good
                      </p>
                    ) : (
                      <ul className="flex flex-col gap-1.5">
                        {validation.issues.map((issue, index) => (
                          <li key={index} className="flex items-start gap-1.5 text-xs text-[var(--danger)]">
                            <CircleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                            {issue.message}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </Card>
                <Card>
                  <div className="flex flex-col gap-2 p-4">
                    <h3 className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">
                      Bullet preview
                    </h3>
                    {bullets.length === 0 ? (
                      <p className="text-xs text-[var(--text-faint)]">No bullets in the body yet.</p>
                    ) : (
                      <ul className="flex flex-col gap-2">
                        {bullets.map((bullet, index) => (
                          <li key={index} className="text-sm text-[var(--text)]">
                            <p>{bullet.text || <span className="text-[var(--text-faint)]">(empty)</span>}</p>
                            {bullet.variants.length > 0 && (
                              <ul className="mt-1 flex flex-col gap-0.5 border-l-2 border-[var(--border)] pl-2">
                                {bullet.variants.map((variant, variantIndex) => (
                                  <li key={variantIndex} className="text-xs text-[var(--text-muted)]">
                                    {variant}
                                  </li>
                                ))}
                              </ul>
                            )}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                </Card>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
