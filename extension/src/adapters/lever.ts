import type { BoardAdapter } from './types';

/**
 * Lever's hosted application form (jobs.lever.co/{company}/{postingId}) asks
 * for a single full name rather than split first/last, and keys its URL
 * fields by `urls[<Label>]`.
 */
export const leverAdapter: BoardAdapter = {
  id: 'lever',
  hostPatterns: [/(^|\.)jobs\.lever\.co$/i],
  fields: {
    fullName: { selector: 'input[name="name"]', kind: 'input' },
    email: { selector: 'input[name="email"]', kind: 'input' },
    phone: { selector: 'input[name="phone"]', kind: 'input' },
    currentCompany: { selector: 'input[name="org"]', kind: 'input' },
    linkedin: { selector: 'input[name="urls[LinkedIn]"]', kind: 'input' },
    github: { selector: 'input[name="urls[GitHub]"]', kind: 'input' },
    portfolio: { selector: 'input[name="urls[Portfolio]"]', kind: 'input' },
    website: { selector: 'input[name="urls[Website]"], input[name="urls[Other]"]', kind: 'input' }
  },
  fileFields: {
    resume: {
      selector: 'input[name="resume"], #resume-upload-input input[type="file"]',
      documentKind: 'resume'
    }
  },
  notes:
    'Lever has no separate first/last name field — "name" is the single full-name input, mapped to fullName rather than firstName/lastName.'
};
