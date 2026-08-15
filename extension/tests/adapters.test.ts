import { describe, expect, it } from 'vitest';
import { matchAdapter } from '../src/adapters';

describe('matchAdapter', () => {
  it('matches every supported board host to its adapter', () => {
    expect(matchAdapter('https://boards.greenhouse.io/acme/jobs/123')?.id).toBe('greenhouse');
    expect(matchAdapter('https://job-boards.greenhouse.io/acme/jobs/123')?.id).toBe('greenhouse');
    expect(matchAdapter('https://jobs.lever.co/acme/abc-123')?.id).toBe('lever');
    expect(matchAdapter('https://jobs.ashbyhq.com/acme/abc')?.id).toBe('ashby');
    expect(matchAdapter('https://jobs.smartrecruiters.com/Acme/123')?.id).toBe('smartrecruiters');
    expect(matchAdapter('https://acme.myworkdayjobs.com/en-US/External/job/123')?.id).toBe('workday');
  });

  it('returns undefined for an unsupported host', () => {
    expect(matchAdapter('https://example.com/careers')).toBeUndefined();
    expect(matchAdapter('not-a-valid-url')).toBeUndefined();
  });
});
