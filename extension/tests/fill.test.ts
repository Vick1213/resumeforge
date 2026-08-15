import { beforeEach, describe, expect, it } from 'vitest';
import { FILLED_ATTR, applyFieldValue, fillTextLike } from '../src/content/fill';
import { FIELD_ID_ATTR } from '../src/content/scan';

function createInput(elementId: string, initialValue = ''): HTMLInputElement {
  const input = document.createElement('input');
  input.type = 'text';
  input.setAttribute(FIELD_ID_ATTR, elementId);
  input.value = initialValue;
  document.body.appendChild(input);
  return input;
}

describe('applyFieldValue — the no-overwrite rule', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
  });

  it('fills an empty field via the native setter and dispatches input/change', () => {
    const input = createInput('rf-1');
    let inputFired = false;
    let changeFired = false;
    input.addEventListener('input', () => {
      inputFired = true;
    });
    input.addEventListener('change', () => {
      changeFired = true;
    });

    const result = applyFieldValue('rf-1', 'jane@example.com');

    expect(result.applied).toBe(true);
    expect(input.value).toBe('jane@example.com');
    expect(input.getAttribute(FILLED_ATTR)).toBe('true');
    expect(inputFired).toBe(true);
    expect(changeFired).toBe(true);
  });

  it('never overwrites a value the user already typed', () => {
    const input = createInput('rf-2', 'user-typed-value');

    const result = applyFieldValue('rf-2', 'profile-value');

    expect(result.applied).toBe(false);
    expect(result.reason).toBe('user-value-present');
    expect(input.value).toBe('user-typed-value');
  });

  it('allows a later run to update a value it previously wrote itself', () => {
    const input = createInput('rf-3');
    fillTextLike(input, 'first-value');
    expect(input.value).toBe('first-value');

    const result = applyFieldValue('rf-3', 'updated-value');

    expect(result.applied).toBe(true);
    expect(input.value).toBe('updated-value');
  });
});
