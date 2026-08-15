import type { BoardAdapter } from './types';

/**
 * Workday-hosted career sites (*.myworkdayjobs.com) render every control as
 * a custom element and identify each one with a `data-automation-id`
 * attribute rather than a conventional `name`/`id`. Those automation ids are
 * stable across tenants because they come from Workday's own component
 * library, not from customer configuration, which is what makes a
 * declarative adapter possible here at all. The actual editable control is
 * usually nested a level or two under the element carrying the attribute,
 * so each selector matches either the attribute owner itself or an
 * input/select/textarea inside it.
 */
export const workdayAdapter: BoardAdapter = {
  id: 'workday',
  hostPatterns: [/(^|\.)myworkdayjobs\.com$/i],
  fields: {
    firstName: {
      selector:
        '[data-automation-id="legalNameSection_firstName"] input, input[data-automation-id="legalNameSection_firstName"]',
      kind: 'input'
    },
    lastName: {
      selector:
        '[data-automation-id="legalNameSection_lastName"] input, input[data-automation-id="legalNameSection_lastName"]',
      kind: 'input'
    },
    email: {
      selector: '[data-automation-id="email"] input, input[data-automation-id="email"]',
      kind: 'input'
    },
    phone: {
      selector:
        '[data-automation-id="phone-number"] input, input[data-automation-id="phone-number"]',
      kind: 'input'
    },
    addressLine1: {
      selector:
        '[data-automation-id="addressSection_addressLine1"] input, input[data-automation-id="addressSection_addressLine1"]',
      kind: 'input'
    },
    addressLine2: {
      selector:
        '[data-automation-id="addressSection_addressLine2"] input, input[data-automation-id="addressSection_addressLine2"]',
      kind: 'input'
    },
    city: {
      selector:
        '[data-automation-id="addressSection_city"] input, input[data-automation-id="addressSection_city"]',
      kind: 'input'
    },
    state: {
      selector:
        '[data-automation-id="addressSection_countryRegion"] input, [data-automation-id="addressSection_countryRegion"] select',
      kind: 'select'
    },
    postalCode: {
      selector:
        '[data-automation-id="addressSection_postalCode"] input, input[data-automation-id="addressSection_postalCode"]',
      kind: 'input'
    },
    country: {
      selector: '[data-automation-id="countryDropdown"] select, select[data-automation-id="countryDropdown"]',
      kind: 'select'
    },
    howDidYouHear: {
      selector:
        '[data-automation-id="source-how-did-you-hear-about-us"] select, [data-automation-id="source-how-did-you-hear-about-us"] input',
      kind: 'select'
    }
  },
  fileFields: {
    resume: {
      selector:
        '[data-automation-id="file-upload-input-ref"] input[type="file"], input[type="file"][data-automation-id*="resume" i]',
      documentKind: 'resume'
    }
  },
  notes:
    'Workday steps (My Information, My Experience, ...) render incrementally as the candidate advances a multi-page wizard; a selector only matches once its step is on screen, which is expected — unresolved fields on later steps are simply rescanned when the user reruns the scan on that step.'
};
