import type { PlannedFill } from './types';

export interface OverlayCallbacks {
  onApply: (accepted: PlannedFill[]) => void;
  onCancel: () => void;
}

const HOST_ID = 'resumeforge-autofill-overlay-host';

const TIER_LABEL: Record<PlannedFill['tier'], string> = {
  adapter: 'adapter',
  heuristic: 'heuristic',
  learned: 'learned',
  model: 'model',
  unresolved: 'unresolved'
};

const STYLE = `
:host { all: initial; }
.rf-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 17, 21, 0.35);
  z-index: 2147483000;
  display: flex;
  justify-content: flex-end;
  align-items: stretch;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
}
.rf-panel {
  width: min(420px, 100vw);
  max-height: 100vh;
  overflow: hidden;
  background: #ffffff;
  color: #17181c;
  display: flex;
  flex-direction: column;
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.25);
}
.rf-header {
  padding: 16px 20px;
  font-size: 15px;
  font-weight: 600;
  border-bottom: 1px solid #e4e5e9;
}
.rf-list {
  flex: 1;
  overflow-y: auto;
  padding: 4px 0;
}
.rf-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 20px;
  border-bottom: 1px solid #f0f1f3;
  cursor: pointer;
}
.rf-row:hover { background: #f7f8fa; }
.rf-row input[type="checkbox"] { margin-top: 3px; flex-shrink: 0; }
.rf-info { min-width: 0; flex: 1; }
.rf-label { font-size: 12px; font-weight: 600; color: #55585f; text-transform: uppercase; letter-spacing: 0.02em; }
.rf-value {
  font-size: 14px;
  color: #17181c;
  overflow-wrap: break-word;
  margin: 2px 0 6px;
}
.rf-meta { display: flex; align-items: center; gap: 8px; font-size: 11px; }
.rf-tier {
  padding: 2px 8px;
  border-radius: 999px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.rf-tier-adapter { background: #dcf5e6; color: #0a7a3c; }
.rf-tier-heuristic { background: #dbeafe; color: #1d4ed8; }
.rf-tier-learned { background: #ede9fe; color: #6d28d9; }
.rf-tier-model { background: #ffedd5; color: #c2410c; }
.rf-tier-unresolved { background: #f1f2f4; color: #6b6f76; }
.rf-confidence { color: #7a7d85; }
.rf-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 14px 20px;
  border-top: 1px solid #e4e5e9;
}
button.rf-apply, button.rf-cancel {
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  padding: 8px 16px;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
}
button.rf-cancel { background: #fff; color: #17181c; border-color: #d7d9dd; }
button.rf-cancel:hover { background: #f5f6f7; }
button.rf-apply { background: #17181c; color: #fff; }
button.rf-apply:hover { background: #34363d; }
button.rf-apply:focus-visible, button.rf-cancel:focus-visible, .rf-row input:focus-visible {
  outline: 2px solid #2563eb;
  outline-offset: 2px;
}
.rf-empty { padding: 24px 20px; color: #6b6f76; font-size: 13px; }
`;

/**
 * A Shadow-DOM preview panel for the fill plan. Nothing on the page is
 * written until the user clicks "Apply selected" — every row starts as a
 * reviewable, individually toggleable line showing which tier of the
 * cascade resolved it, so the cascade is observable rather than a black
 * box. Escape and the Cancel button both discard the plan without touching
 * the page.
 */
export class AutofillOverlay {
  private hostEl: HTMLElement | null = null;
  private shadow: ShadowRoot | null = null;
  private plan: PlannedFill[] = [];
  private readonly callbacks: OverlayCallbacks;
  private readonly handleKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.callbacks.onCancel();
      this.close();
    }
  };

  constructor(callbacks: OverlayCallbacks) {
    this.callbacks = callbacks;
  }

  show(plan: readonly PlannedFill[]): void {
    this.close();
    this.plan = plan.map((row) => ({ ...row }));

    this.hostEl = document.createElement('div');
    this.hostEl.id = HOST_ID;
    this.shadow = this.hostEl.attachShadow({ mode: 'open' });
    document.documentElement.appendChild(this.hostEl);

    this.render();
    document.addEventListener('keydown', this.handleKeydown, true);
  }

  close(): void {
    if (this.hostEl) {
      this.hostEl.remove();
    }
    this.hostEl = null;
    this.shadow = null;
    document.removeEventListener('keydown', this.handleKeydown, true);
  }

  get isOpen(): boolean {
    return this.hostEl !== null;
  }

  private render(): void {
    if (!this.shadow) return;
    this.shadow.replaceChildren();

    const style = document.createElement('style');
    style.textContent = STYLE;
    this.shadow.appendChild(style);

    const backdrop = document.createElement('div');
    backdrop.className = 'rf-backdrop';

    const panel = document.createElement('div');
    panel.className = 'rf-panel';
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    panel.setAttribute('aria-label', 'ResumeForge Autofill preview');
    panel.tabIndex = -1;

    const header = document.createElement('div');
    header.className = 'rf-header';
    header.textContent =
      this.plan.length === 0
        ? 'ResumeForge Autofill — no fillable fields found'
        : `ResumeForge Autofill — ${this.plan.length} field${this.plan.length === 1 ? '' : 's'} found`;
    panel.appendChild(header);

    const list = document.createElement('div');
    list.className = 'rf-list';
    if (this.plan.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'rf-empty';
      empty.textContent = 'Nothing to fill on this page.';
      list.appendChild(empty);
    } else {
      this.plan.forEach((row, index) => list.appendChild(this.renderRow(row, index)));
    }
    panel.appendChild(list);

    panel.appendChild(this.renderFooter());
    backdrop.appendChild(panel);
    this.shadow.appendChild(backdrop);

    panel.focus();
  }

  private renderRow(row: PlannedFill, index: number): HTMLElement {
    const rowEl = document.createElement('label');
    rowEl.className = 'rf-row';

    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.checked = row.accepted;
    checkbox.setAttribute('aria-label', `Accept fill for ${row.label ?? row.canonicalKey ?? 'field'}`);
    checkbox.addEventListener('change', () => {
      const target = this.plan[index];
      if (target) target.accepted = checkbox.checked;
    });

    const info = document.createElement('div');
    info.className = 'rf-info';

    const labelEl = document.createElement('div');
    labelEl.className = 'rf-label';
    labelEl.textContent = row.label ?? row.canonicalKey ?? '(unlabeled field)';

    const valueEl = document.createElement('div');
    valueEl.className = 'rf-value';
    valueEl.textContent = row.value || '(no value on file)';

    const meta = document.createElement('div');
    meta.className = 'rf-meta';

    const tierBadge = document.createElement('span');
    tierBadge.className = `rf-tier rf-tier-${row.tier}`;
    tierBadge.textContent = TIER_LABEL[row.tier];

    const confidenceEl = document.createElement('span');
    confidenceEl.className = 'rf-confidence';
    confidenceEl.textContent = `${Math.round(row.confidence * 100)}% confidence`;

    meta.appendChild(tierBadge);
    meta.appendChild(confidenceEl);

    info.appendChild(labelEl);
    info.appendChild(valueEl);
    info.appendChild(meta);

    rowEl.appendChild(checkbox);
    rowEl.appendChild(info);
    return rowEl;
  }

  private renderFooter(): HTMLElement {
    const footer = document.createElement('div');
    footer.className = 'rf-footer';

    const cancelBtn = document.createElement('button');
    cancelBtn.type = 'button';
    cancelBtn.className = 'rf-cancel';
    cancelBtn.textContent = 'Cancel';
    cancelBtn.addEventListener('click', () => {
      this.callbacks.onCancel();
      this.close();
    });

    const applyBtn = document.createElement('button');
    applyBtn.type = 'button';
    applyBtn.className = 'rf-apply';
    applyBtn.textContent = 'Apply selected';
    applyBtn.addEventListener('click', () => {
      const accepted = this.plan.filter((row) => row.accepted);
      this.callbacks.onApply(accepted);
      this.close();
    });

    footer.appendChild(cancelBtn);
    footer.appendChild(applyBtn);
    return footer;
  }
}
