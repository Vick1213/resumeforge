import type { AutofillInputType, CanonicalKey, ResolutionTier } from '../contracts';

/**
 * Everything the scanner (`scan.ts`) extracts about one fillable control.
 * This is the unit the heuristic matcher, the board adapters (indirectly,
 * via the element they resolved), and the overlay all operate on.
 */
export interface FieldDescriptor {
  /** Stable id assigned at scan time; see `scan.ts` for how it's derived. */
  elementId: string;
  label: string | null;
  name: string | null;
  id: string | null;
  placeholder: string | null;
  ariaLabel: string | null;
  autoComplete: string | null;
  inputType: AutofillInputType;
  /** Visible option text, for select/radio groups. */
  options: string[];
}

/** A file to attach, resolved from `AutofillProfile.documents` by a board adapter. */
export interface PlannedFileFill {
  documentKind: 'resume' | 'coverLetter';
  fileName: string;
  downloadUrl: string;
}

/** One row of the fill plan shown in the overlay before anything is applied. */
export interface PlannedFill {
  elementId: string;
  label: string | null;
  canonicalKey: CanonicalKey | '';
  /** Display value; for a file row this is the file name. */
  value: string;
  tier: ResolutionTier;
  confidence: number;
  /** True once the user has toggled this row on in the overlay. */
  accepted: boolean;
  /** Present for file-input rows; absent for ordinary value rows. */
  file?: PlannedFileFill;
}
