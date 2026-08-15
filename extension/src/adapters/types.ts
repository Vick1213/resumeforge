import type { CanonicalKey } from '../contracts';

/** The kinds of controls a `FieldSelector` can point at. */
export type FieldSelectorKind = 'input' | 'select' | 'radio' | 'textarea' | 'file';

/**
 * A declarative pointer at a form control for one canonical key on one board.
 * Adapters are pure data — nothing here executes anything against the page.
 * `src/content/fill.ts` is the only module that interprets a `FieldSelector`
 * and touches the DOM.
 */
export interface FieldSelector {
  /** CSS selector, evaluated with `document.querySelector`/`querySelectorAll`. */
  selector: string;
  kind: FieldSelectorKind;
  /**
   * For `radio` (and `select`, when the desired option must be chosen by
   * text rather than by value): given an option's visible text, return true
   * if it is the option to select. Optional — most fields don't need it.
   */
  optionMatch?: (optionText: string) => boolean;
}

/** Where to attach an uploaded file (resume, cover letter, ...). */
export interface FileFieldSelector {
  selector: string;
  /** Which document kind from `AutofillProfile.documents` to attach. */
  documentKind: 'resume' | 'coverLetter';
}

/**
 * One board's declarative selector map. Adding a new supported board means
 * adding a new module shaped like this and registering it in `index.ts` —
 * never touching the filler, matcher, or overlay.
 */
export interface BoardAdapter {
  id: string;
  hostPatterns: RegExp[];
  fields: Partial<Record<CanonicalKey, FieldSelector>>;
  fileFields?: Partial<Record<'resume' | 'coverLetter', FileFieldSelector>>;
  notes?: string;
}
