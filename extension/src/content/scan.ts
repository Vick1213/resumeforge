import type { AutofillInputType } from '../contracts';
import { escapeAttributeValue } from '../lib/cssEscape';
import { fnv1a } from '../lib/hash';
import type { FieldDescriptor } from './types';

/**
 * Marker attribute the scanner stamps onto every control it describes, so a
 * `FieldDescriptor` (plain, serializable data — safe to send through
 * `chrome.runtime` messaging) can be mapped back to its live DOM element(s)
 * later, in `fill.ts`, without ever passing element references around.
 */
export const FIELD_ID_ATTR = 'data-resumeforge-id';

/**
 * Normalizes a label the same way the heuristic matcher would tokenize
 * around it — lowercased, whitespace collapsed — so trivial copy edits
 * (extra spacing, a re-cased word) don't change a field's fingerprint.
 */
function normalizeLabel(label: string | null): string {
  return (label ?? '').toLowerCase().replace(/\s+/g, ' ').trim();
}

/**
 * A content-derived fingerprint for one field: everything that identifies
 * what the field *is*, independent of where it happens to sit in the DOM.
 * This is deliberately NOT based on traversal order — see `elementId`
 * below for why that distinction matters.
 */
function fieldFingerprint(params: {
  name: string | null;
  id: string | null;
  autoComplete: string | null;
  inputType: AutofillInputType;
  label: string | null;
}): string {
  const key = [
    params.name ?? '',
    params.id ?? '',
    params.autoComplete ?? '',
    params.inputType,
    normalizeLabel(params.label)
  ].join('|');
  return fnv1a(key);
}

/**
 * Assigns `elementId`s from content fingerprints rather than a traversal
 * counter. `computeFormSignature` below is intentionally order-independent
 * (it sorts before hashing) so the same logical form matches on every
 * visit; if `elementId` were instead a sequential counter, two renders of
 * the identical field set in a different DOM order (a conditional field
 * appearing before vs. after another, for instance) would produce the same
 * `formSignature` but bind each elementId to a *different* field. A learned
 * field map keyed by `(host, formSignature)` would then look like a hit and
 * apply the wrong canonical key to the wrong field — silently, and only on
 * the reuse path the learned map exists for. Deriving elementId from field
 * content instead means the id only changes when the field itself changes,
 * regardless of where it sits on the page.
 *
 * Genuinely identical fingerprints are still possible (e.g. two unlabelled
 * text inputs with no name/id/autocomplete), so ties are broken by
 * occurrence order among same-fingerprint fields: the first occurrence gets
 * `rf-<hash>`, the second `rf-<hash>-1`, the third `rf-<hash>-2`, and so on.
 * That residual order-sensitivity is confined to the genuinely ambiguous
 * case instead of being the default for every field.
 */
class ElementIdAssigner {
  private readonly occurrenceCounts = new Map<string, number>();

  assign(fingerprint: string): string {
    const occurrence = (this.occurrenceCounts.get(fingerprint) ?? 0) + 1;
    this.occurrenceCounts.set(fingerprint, occurrence);
    return occurrence === 1 ? `rf-${fingerprint}` : `rf-${fingerprint}-${occurrence - 1}`;
  }
}

const FILLABLE_INPUT_TYPES = new Set([
  'text',
  'email',
  'tel',
  'number',
  'date',
  'search',
  'url',
  'checkbox',
  'radio',
  'file',
  ''
]);

function isFillableInput(el: HTMLInputElement): boolean {
  const type = (el.getAttribute('type') || 'text').toLowerCase();
  return FILLABLE_INPUT_TYPES.has(type);
}

function toAutofillInputType(
  el: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
): AutofillInputType {
  if (el instanceof HTMLSelectElement) return 'select';
  if (el instanceof HTMLTextAreaElement) return 'textarea';
  const type = (el.getAttribute('type') || 'text').toLowerCase();
  if (type === 'radio') return 'radio';
  if (type === 'file') return 'file';
  if (type === 'checkbox') return 'checkbox';
  if (type === 'email') return 'email';
  if (type === 'tel') return 'tel';
  return 'text';
}

function isVisible(el: HTMLElement): boolean {
  if (el.hidden) return false;
  const style = window.getComputedStyle(el);
  return style.display !== 'none' && style.visibility !== 'hidden';
}

function textOf(el: Element | null): string | null {
  const text = el?.textContent?.replace(/\s+/g, ' ').trim();
  return text && text.length > 0 ? text : null;
}

/**
 * The label cascade from CONTRACTS-adjacent spec: `<label for>` first, then
 * a wrapping `<label>`, then `aria-labelledby`, then `aria-label`, then the
 * nearest preceding text in the same field container as a last resort.
 */
export function getLabelText(el: HTMLElement): string | null {
  if (el.id) {
    const forLabel = document.querySelector(`label[for="${escapeAttributeValue(el.id)}"]`);
    const text = textOf(forLabel);
    if (text) return text;
  }

  const wrapping = el.closest('label');
  const wrappingText = textOf(wrapping);
  if (wrappingText) return wrappingText;

  const labelledBy = el.getAttribute('aria-labelledby');
  if (labelledBy) {
    const text = labelledBy
      .split(/\s+/)
      .map((id) => textOf(document.getElementById(id)))
      .filter((t): t is string => Boolean(t))
      .join(' ');
    if (text) return text;
  }

  const ariaLabel = el.getAttribute('aria-label');
  if (ariaLabel && ariaLabel.trim().length > 0) return ariaLabel.trim();

  // Last resort: walk up a few container levels looking for the nearest
  // preceding sibling that reads like a label rather than another control.
  let container: HTMLElement | null = el.parentElement;
  for (let depth = 0; depth < 3 && container; depth++) {
    let sibling: Element | null = el.previousElementSibling ?? container.firstElementChild;
    while (sibling && sibling !== el) {
      if (!['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(sibling.tagName)) {
        const text = textOf(sibling);
        if (text) return text;
      }
      sibling = sibling.nextElementSibling;
    }
    container = container.parentElement;
  }

  return null;
}

function optionsOf(el: HTMLSelectElement): string[] {
  return Array.from(el.options)
    .map((o) => o.textContent?.trim() ?? '')
    .filter((t) => t.length > 0);
}

function radioGroupOptions(name: string, root: ParentNode): string[] {
  const radios = Array.from(
    root.querySelectorAll<HTMLInputElement>(`input[type="radio"][name="${escapeAttributeValue(name)}"]`)
  );
  return radios
    .map((r) => getLabelText(r))
    .filter((t): t is string => Boolean(t));
}

/**
 * Walks `root` for fillable controls and builds a `FieldDescriptor` for
 * each, assigning a content-derived, DOM-order-independent `elementId` (see
 * `ElementIdAssigner`) and stamping `FIELD_ID_ATTR` onto the live
 * element(s) so `fill.ts` can find them again later. Radio button groups
 * collapse into a single descriptor (one canonical value, one choice among
 * the group's options), matching how `ResolveFieldsRequest` models `radio`
 * as one field with `options`.
 */
export function scanPage(root: ParentNode = document): FieldDescriptor[] {
  const descriptors: FieldDescriptor[] = [];
  const seenRadioGroups = new Set<string>();
  const elementIds = new ElementIdAssigner();

  const controls = root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
    'input, select, textarea'
  );

  for (const el of Array.from(controls)) {
    if (!(el instanceof HTMLElement) || !isVisible(el)) continue;
    if (el instanceof HTMLInputElement && !isFillableInput(el)) continue;
    if (el.hasAttribute(FIELD_ID_ATTR)) continue; // already scanned (radio group member)

    const inputType = toAutofillInputType(el);

    if (inputType === 'radio') {
      const name = (el as HTMLInputElement).name;
      if (!name || seenRadioGroups.has(name)) continue;
      seenRadioGroups.add(name);

      const label = getLabelText(el);
      const autoComplete = el.getAttribute('autocomplete');
      const elementId = elementIds.assign(
        fieldFingerprint({ name, id: el.id || null, autoComplete, inputType: 'radio', label })
      );
      const groupMembers = root.querySelectorAll<HTMLInputElement>(
        `input[type="radio"][name="${escapeAttributeValue(name)}"]`
      );
      groupMembers.forEach((member) => member.setAttribute(FIELD_ID_ATTR, elementId));

      descriptors.push({
        elementId,
        label,
        name,
        id: el.id || null,
        placeholder: null,
        ariaLabel: el.getAttribute('aria-label'),
        autoComplete,
        inputType: 'radio',
        options: radioGroupOptions(name, root)
      });
      continue;
    }

    const name = (el as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).name || null;
    const label = getLabelText(el);
    const autoComplete = el.getAttribute('autocomplete');
    const elementId = elementIds.assign(
      fieldFingerprint({ name, id: el.id || null, autoComplete, inputType, label })
    );
    el.setAttribute(FIELD_ID_ATTR, elementId);

    descriptors.push({
      elementId,
      label,
      name,
      id: el.id || null,
      placeholder: (el as HTMLInputElement | HTMLTextAreaElement).placeholder || null,
      ariaLabel: el.getAttribute('aria-label'),
      autoComplete,
      inputType,
      options: el instanceof HTMLSelectElement ? optionsOf(el) : []
    });
  }

  return descriptors;
}

/**
 * A stable fingerprint for a set of form fields: an FNV-1a hash over the
 * sorted `name|id|label` tuples. Sorting makes it independent of DOM order,
 * so the same logical form yields the same signature on every visit; adding
 * or removing a field changes the tuple set and therefore the hash.
 */
export function computeFormSignature(
  fields: readonly Pick<FieldDescriptor, 'name' | 'id' | 'label'>[]
): string {
  const tuples = fields.map((f) => `${f.name ?? ''}|${f.id ?? ''}|${f.label ?? ''}`);
  tuples.sort();
  return fnv1a(tuples.join('\n'));
}
