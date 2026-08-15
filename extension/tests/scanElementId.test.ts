import { beforeEach, describe, expect, it } from 'vitest';
import { computeFormSignature, scanPage } from '../src/content/scan';

function setBody(html: string): void {
  document.body.innerHTML = html;
}

describe('scanPage elementId stability', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  it('assigns the same formSignature and the same per-field elementId regardless of DOM order', () => {
    setBody(`
      <input name="first_name" id="first_name" aria-label="First name" />
      <input name="email" id="email" aria-label="Email" />
      <input name="phone" id="phone" aria-label="Phone" />
    `);
    const inOrder = scanPage(document.body);
    const inOrderSignature = computeFormSignature(inOrder);

    setBody(`
      <input name="phone" id="phone" aria-label="Phone" />
      <input name="first_name" id="first_name" aria-label="First name" />
      <input name="email" id="email" aria-label="Email" />
    `);
    const shuffled = scanPage(document.body);
    const shuffledSignature = computeFormSignature(shuffled);

    // This is the exact invariant that broke: formSignature is deliberately
    // order-independent, so elementId must be too, or a learned map keyed
    // by (host, formSignature) would bind the right keys to the wrong
    // fields whenever a conditional field changes rendering order.
    expect(shuffledSignature).toBe(inOrderSignature);

    for (const name of ['first_name', 'email', 'phone']) {
      const before = inOrder.find((d) => d.name === name);
      const after = shuffled.find((d) => d.name === name);
      expect(before).toBeDefined();
      expect(after).toBeDefined();
      expect(after?.elementId).toBe(before?.elementId);
    }
  });

  it('disambiguates identical fingerprints with a stable occurrence-index suffix', () => {
    setBody(`
      <input type="text" />
      <input type="text" />
      <input type="text" />
    `);

    const firstScan = scanPage(document.body);
    expect(firstScan).toHaveLength(3);

    const ids = firstScan.map((d) => d.elementId);
    expect(new Set(ids).size).toBe(3); // distinct despite identical fingerprints

    const [base, second, third] = ids;
    expect(second).toBe(`${base}-1`);
    expect(third).toBe(`${base}-2`);

    // Rescanning the identical markup must reproduce the exact same ids —
    // the occurrence-index suffix is derived from DOM order among
    // same-fingerprint fields only, not from anything that varies run to
    // run.
    document
      .querySelectorAll('[data-resumeforge-id]')
      .forEach((el) => el.removeAttribute('data-resumeforge-id'));
    const secondScan = scanPage(document.body);
    expect(secondScan.map((d) => d.elementId)).toEqual(ids);
  });

  it("changes only the affected field's elementId when its label changes", () => {
    setBody(`
      <input name="first_name" id="first_name" aria-label="First name" />
      <input name="email" id="email" aria-label="Email" />
    `);
    const before = scanPage(document.body);

    setBody(`
      <input name="first_name" id="first_name" aria-label="Legal first name" />
      <input name="email" id="email" aria-label="Email" />
    `);
    const after = scanPage(document.body);

    const beforeFirstName = before.find((d) => d.name === 'first_name');
    const afterFirstName = after.find((d) => d.name === 'first_name');
    const beforeEmail = before.find((d) => d.name === 'email');
    const afterEmail = after.find((d) => d.name === 'email');

    expect(afterFirstName?.elementId).not.toBe(beforeFirstName?.elementId);
    expect(afterEmail?.elementId).toBe(beforeEmail?.elementId);
  });
});
