import type { BoardAdapter } from './types';

/**
 * Greenhouse application forms (both the legacy embedded form on
 * boards.greenhouse.io and the newer job-boards.greenhouse.io "Job Board 2.0"
 * templates) use predictable field `id`s or `name`s for the standard
 * candidate fields. Free-form custom questions ("Why do you want to work
 * here?") get per-posting dynamic ids and are intentionally left out of this
 * map — they fall through to the heuristic matcher (tier 2) or the model
 * fallback (tier 3).
 */
export const greenhouseAdapter: BoardAdapter = {
  id: 'greenhouse',
  hostPatterns: [/(^|\.)boards\.greenhouse\.io$/i, /(^|\.)job-boards\.greenhouse\.io$/i],
  fields: {
    firstName: {
      selector: '#first_name, input[name="job_application[first_name]"], input[name="first_name"]',
      kind: 'input'
    },
    lastName: {
      selector: '#last_name, input[name="job_application[last_name]"], input[name="last_name"]',
      kind: 'input'
    },
    email: {
      selector: '#email, input[name="job_application[email]"], input[name="email"]',
      kind: 'input'
    },
    phone: {
      selector: '#phone, input[name="job_application[phone]"], input[name="phone"]',
      kind: 'input'
    },
    linkedin: {
      selector:
        'input[name="job_application[urls][LinkedIn]"], input[name*="urls][LinkedIn" i], #linkedin',
      kind: 'input'
    },
    website: {
      selector: 'input[name="job_application[urls][Website]"], input[name*="urls][Website" i]',
      kind: 'input'
    }
  },
  fileFields: {
    resume: {
      selector: '#resume, input[name="job_application[resume]"], input[type="file"][id*="resume" i]',
      documentKind: 'resume'
    },
    coverLetter: {
      selector:
        '#cover_letter, input[name="job_application[cover_letter]"], input[type="file"][id*="cover_letter" i]',
      documentKind: 'coverLetter'
    }
  },
  notes:
    'Custom screening questions get per-posting dynamic element ids and are not addressable declaratively; they resolve via the heuristic matcher or the model fallback.'
};
