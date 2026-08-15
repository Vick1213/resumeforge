import { useState } from 'react';
import { FileDown, FileCode, FileText } from 'lucide-react';
import { Button } from '@/components/ui/Button';
import { useRenderResume } from '@/api/queries';
import type { RenderFormat } from '@/api/types';

const FORMATS: { format: RenderFormat; label: string; icon: typeof FileDown }[] = [
  { format: 'pdf', label: 'PDF', icon: FileDown },
  { format: 'html', label: 'HTML', icon: FileCode },
  { format: 'md', label: 'Markdown', icon: FileText },
];

export interface ExportButtonsProps {
  resumeId: string;
  fileBaseName: string;
}

export function ExportButtons({ resumeId, fileBaseName }: ExportButtonsProps) {
  const renderResume = useRenderResume();
  const [pendingFormat, setPendingFormat] = useState<RenderFormat | null>(null);

  async function handleExport(format: RenderFormat): Promise<void> {
    setPendingFormat(format);
    try {
      const blob = await renderResume.mutateAsync({ resumeId, format });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `${fileBaseName}.${format}`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
    } finally {
      setPendingFormat(null);
    }
  }

  return (
    <div className="flex flex-wrap gap-2">
      {FORMATS.map(({ format, label, icon: Icon }) => (
        <Button key={format} variant="outline" size="sm" disabled={pendingFormat !== null} onClick={() => void handleExport(format)}>
          <Icon className="h-4 w-4" aria-hidden="true" />
          {pendingFormat === format ? 'Exporting…' : `Export ${label}`}
        </Button>
      ))}
    </div>
  );
}
