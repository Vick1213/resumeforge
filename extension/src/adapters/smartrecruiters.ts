import type { BoardAdapter } from './types';

/**
 * SmartRecruiters' hosted apply form (jobs.smartrecruiters.com) uses
 * camelCase `name` attributes for its standard candidate fields.
 */
export const smartRecruitersAdapter: BoardAdapter = {
  id: 'smartrecruiters',
  hostPatterns: [/(^|\.)jobs\.smartrecruiters\.com$/i],
  fields: {
    firstName: { selector: 'input[name="firstName"], #first-name', kind: 'input' },
    lastName: { selector: 'input[name="lastName"], #last-name', kind: 'input' },
    email: { selector: 'input[name="email"], #email', kind: 'input' },
    phone: { selector: 'input[name="phone"], input[name="mobile"], #phone', kind: 'input' },
    linkedin: { selector: 'input[name="linkedInUrl"], input[name="linkedin"]', kind: 'input' },
    city: { selector: 'input[name="city"]', kind: 'input' },
    country: { selector: 'select[name="country"]', kind: 'select' }
  },
  fileFields: {
    resume: {
      selector: 'input[type="file"][name="resume"], input[type="file"]#resume',
      documentKind: 'resume'
    }
  }
};
