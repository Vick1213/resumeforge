import { describe, expect, it } from 'vitest';
import { computeFormSignature } from '../src/content/scan';

const firstName = { name: 'first_name', id: null, label: 'First Name' };
const lastName = { name: 'last_name', id: null, label: 'Last Name' };
const email = { name: 'email', id: null, label: 'Email' };
const baseFields = [firstName, lastName, email];

describe('computeFormSignature', () => {
  it('is stable for the same field set scanned twice', () => {
    const a = computeFormSignature(baseFields);
    const b = computeFormSignature(baseFields.map((f) => ({ ...f })));
    expect(a).toBe(b);
  });

  it('is independent of field order', () => {
    const shuffled = [email, firstName, lastName];
    expect(computeFormSignature(shuffled)).toBe(computeFormSignature(baseFields));
  });

  it('changes when the field set changes', () => {
    const phone = { name: 'phone', id: null, label: 'Phone' };
    const withExtraField = [...baseFields, phone];
    expect(computeFormSignature(withExtraField)).not.toBe(computeFormSignature(baseFields));
  });
});
