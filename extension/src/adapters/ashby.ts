import type { BoardAdapter } from './types';

/**
 * Ashby (jobs.ashbyhq.com) renders its built-in "system fields" with stable
 * ids/names prefixed `_systemfield_`; everything else on the form is a
 * custom question with a randomly generated field id that only the
 * heuristic matcher or the model fallback can resolve.
 */
export const ashbyAdapter: BoardAdapter = {
  id: 'ashby',
  hostPatterns: [/(^|\.)jobs\.ashbyhq\.com$/i],
  fields: {
    fullName: {
      selector: '#_systemfield_name, input[name="_systemfield_name"]',
      kind: 'input'
    },
    email: {
      selector: '#_systemfield_email, input[name="_systemfield_email"]',
      kind: 'input'
    },
    phone: {
      selector: '#_systemfield_phone, input[name="_systemfield_phone"]',
      kind: 'input'
    },
    city: {
      selector: '#_systemfield_location, input[name="_systemfield_location"]',
      kind: 'input'
    },
    linkedin: {
      selector: 'input[name*="linkedin" i]',
      kind: 'input'
    }
  },
  fileFields: {
    resume: {
      selector:
        'input[type="file"]#_systemfield_resume, input[type="file"][name="_systemfield_resume"], input[type="file"][name*="resume" i]',
      documentKind: 'resume'
    }
  },
  notes:
    'Ashby location is a single free-text field, mapped to city as a best effort; state/country from it are left for the heuristic or model tier.'
};
