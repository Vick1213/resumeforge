import { tokenize } from '../heuristics/matcher';
import { escapeAttributeValue } from '../lib/cssEscape';
import { FIELD_ID_ATTR, getLabelText } from './scan';

/**
 * Marks a control as having been written by this extension, so a later fill
 * pass can tell "empty because the user hasn't touched it" apart from
 * "empty because we haven't filled it yet" and, crucially, can tell its own
 * previous writes apart from the user's own typing. Never overwrite a field
 * whose current value is non-empty and NOT marked with this attribute.
 */
export const FILLED_ATTR = 'data-resumeforge-filled';

export type FillReason =
  | 'element-not-found'
  | 'user-value-present'
  | 'no-confident-option-match'
  | 'unsupported-control';

export interface FillResult {
  elementId: string;
  applied: boolean;
  reason?: FillReason;
}

/** Finds every live element the scanner stamped with this elementId. */
export function findFieldElements(elementId: string, root: ParentNode = document): HTMLElement[] {
  return Array.from(
    root.querySelectorAll<HTMLElement>(`[${FIELD_ID_ATTR}="${escapeAttributeValue(elementId)}"]`)
  );
}

/**
 * Sets `.value` through the framework the page's own `<input>` element uses
 * internally (the native property setter) rather than the instance's own
 * `.value` assignment, then fires `input`/`change`. Plain `el.value = v`
 * does not notify React/Vue-controlled inputs because their virtual DOM
 * diffing bypasses the instance setter they've shadowed; going through the
 * prototype's setter and dispatching real events is what makes those
 * frameworks pick the value up.
 */
export function setControlValue(
  el: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
  value: string
): void {
  const proto =
    el instanceof HTMLTextAreaElement
      ? window.HTMLTextAreaElement.prototype
      : el instanceof HTMLSelectElement
        ? window.HTMLSelectElement.prototype
        : window.HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
  if (setter) {
    setter.call(el, value);
  } else {
    el.value = value;
  }
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
}

/**
 * True when the control already holds a value the user typed — i.e. it's
 * non-empty and this extension didn't write it. The filler must never
 * overwrite that value.
 */
export function hasUnprotectedUserValue(
  el: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement
): boolean {
  const filledByUs = el.getAttribute(FILLED_ATTR) === 'true';
  return el.value.trim().length > 0 && !filledByUs;
}

/** Fills a text-like input or a textarea, respecting the no-overwrite rule. */
export function fillTextLike(
  el: HTMLInputElement | HTMLTextAreaElement,
  value: string
): FillResult {
  const elementId = el.getAttribute(FIELD_ID_ATTR) ?? '';
  if (hasUnprotectedUserValue(el)) {
    return { elementId, applied: false, reason: 'user-value-present' };
  }
  setControlValue(el, value);
  el.setAttribute(FILLED_ATTR, 'true');
  return { elementId, applied: true };
}

/**
 * Fuzzy-matches `target` against a list of option texts. Tries an exact
 * case-insensitive match, then substring containment either direction, then
 * token overlap — returning `null` (never a guess) when nothing clears a
 * reasonable overlap bar, so the caller can leave the control untouched
 * rather than pick the wrong option.
 */
export function fuzzyMatchOption(options: readonly string[], target: string): string | null {
  const normalizedTarget = target.trim().toLowerCase();
  if (!normalizedTarget) return null;

  const exact = options.find((o) => o.trim().toLowerCase() === normalizedTarget);
  if (exact) return exact;

  const contains = options.find((o) => {
    const norm = o.trim().toLowerCase();
    return norm.length > 0 && (norm.includes(normalizedTarget) || normalizedTarget.includes(norm));
  });
  if (contains) return contains;

  const targetTokens = new Set(tokenize(normalizedTarget));
  if (targetTokens.size === 0) return null;

  const MIN_OPTION_OVERLAP = 0.5;
  let best: { option: string; score: number } | null = null;
  for (const option of options) {
    const optionTokens = new Set(tokenize(option));
    if (optionTokens.size === 0) continue;
    const intersectionSize = [...targetTokens].filter((t) => optionTokens.has(t)).length;
    const unionSize = new Set([...targetTokens, ...optionTokens]).size;
    const score = unionSize === 0 ? 0 : intersectionSize / unionSize;
    if (score > (best?.score ?? 0)) best = { option, score };
  }

  return best && best.score >= MIN_OPTION_OVERLAP ? best.option : null;
}

/** Fills a `<select>` by fuzzy-matching `value` against its option text. */
export function fillSelect(el: HTMLSelectElement, value: string): FillResult {
  const elementId = el.getAttribute(FIELD_ID_ATTR) ?? '';
  if (hasUnprotectedUserValue(el)) {
    return { elementId, applied: false, reason: 'user-value-present' };
  }

  const optionTexts = Array.from(el.options).map((o) => o.textContent?.trim() ?? '');
  const matchedText = fuzzyMatchOption(optionTexts, value);
  if (matchedText === null) {
    return { elementId, applied: false, reason: 'no-confident-option-match' };
  }

  const option = Array.from(el.options).find((o) => (o.textContent?.trim() ?? '') === matchedText);
  if (!option) {
    return { elementId, applied: false, reason: 'no-confident-option-match' };
  }

  setControlValue(el, option.value);
  el.setAttribute(FILLED_ATTR, 'true');
  return { elementId, applied: true };
}

/**
 * Fills a radio group by fuzzy-matching `value` against each member's
 * label. Never overwrites a group the user has already made a choice in.
 */
export function fillRadioGroup(elements: readonly HTMLInputElement[], value: string): FillResult {
  const elementId = elements[0]?.getAttribute(FIELD_ID_ATTR) ?? '';
  if (elements.length === 0) {
    return { elementId, applied: false, reason: 'element-not-found' };
  }

  const alreadyChosenByUser = elements.some(
    (el) => el.checked && el.getAttribute(FILLED_ATTR) !== 'true'
  );
  if (alreadyChosenByUser) {
    return { elementId, applied: false, reason: 'user-value-present' };
  }

  const labels = elements.map((el) => getLabelText(el) ?? '');
  const matchedLabel = fuzzyMatchOption(labels, value);
  if (matchedLabel === null) {
    return { elementId, applied: false, reason: 'no-confident-option-match' };
  }

  const target = elements.find((el, i) => labels[i] === matchedLabel);
  if (!target) {
    return { elementId, applied: false, reason: 'no-confident-option-match' };
  }

  const checkedSetter = Object.getOwnPropertyDescriptor(
    window.HTMLInputElement.prototype,
    'checked'
  )?.set;
  if (checkedSetter) {
    checkedSetter.call(target, true);
  } else {
    target.checked = true;
  }
  target.dispatchEvent(new Event('input', { bubbles: true }));
  target.dispatchEvent(new Event('change', { bubbles: true }));
  elements.forEach((el) => el.setAttribute(FILLED_ATTR, 'true'));

  return { elementId, applied: true };
}

/**
 * Attaches a file to a file input via `DataTransfer`, the only way to set
 * `.files` on an `<input type="file">` programmatically. `file` is expected
 * to come from a blob fetched from the backend's document download URL
 * (see `background/service-worker.ts`).
 */
export function fillFileInput(el: HTMLInputElement, file: File): FillResult {
  const elementId = el.getAttribute(FIELD_ID_ATTR) ?? '';
  if (el.files && el.files.length > 0 && el.getAttribute(FILLED_ATTR) !== 'true') {
    return { elementId, applied: false, reason: 'user-value-present' };
  }

  const dataTransfer = new DataTransfer();
  dataTransfer.items.add(file);
  el.files = dataTransfer.files;
  el.setAttribute(FILLED_ATTR, 'true');
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  return { elementId, applied: true };
}

/** Fetches a document from the backend and wraps it as a `File` for `fillFileInput`. */
export async function fetchAsFile(url: string, fileName: string, mimeType?: string): Promise<File> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Failed to download ${fileName}: ${response.status} ${response.statusText}`);
  }
  const blob = await response.blob();
  return new File([blob], fileName, { type: mimeType ?? blob.type });
}

/**
 * Applies one resolved value to whatever live element(s) the scanner
 * associated with `elementId`, dispatching to the right strategy for the
 * control kind found on the page right now. This function never submits a
 * form and never clicks a button — it only ever writes to the field(s) it
 * was explicitly asked to fill.
 */
export function applyFieldValue(
  elementId: string,
  value: string,
  root: ParentNode = document
): FillResult {
  const elements = findFieldElements(elementId, root);
  if (elements.length === 0) {
    return { elementId, applied: false, reason: 'element-not-found' };
  }

  const first = elements[0];
  if (!first) {
    return { elementId, applied: false, reason: 'element-not-found' };
  }

  if (first instanceof HTMLInputElement && first.type === 'radio') {
    return fillRadioGroup(elements as HTMLInputElement[], value);
  }
  if (first instanceof HTMLSelectElement) {
    return fillSelect(first, value);
  }
  if (first instanceof HTMLInputElement || first instanceof HTMLTextAreaElement) {
    if (first instanceof HTMLInputElement && first.type === 'file') {
      return { elementId, applied: false, reason: 'unsupported-control' };
    }
    return fillTextLike(first, value);
  }

  return { elementId, applied: false, reason: 'unsupported-control' };
}
