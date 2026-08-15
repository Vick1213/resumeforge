import type { BoardAdapter } from './types';
import { greenhouseAdapter } from './greenhouse';
import { leverAdapter } from './lever';
import { ashbyAdapter } from './ashby';
import { smartRecruitersAdapter } from './smartrecruiters';
import { workdayAdapter } from './workday';

export type { BoardAdapter, FieldSelector, FieldSelectorKind, FileFieldSelector } from './types';
export { greenhouseAdapter } from './greenhouse';
export { leverAdapter } from './lever';
export { ashbyAdapter } from './ashby';
export { smartRecruitersAdapter } from './smartrecruiters';
export { workdayAdapter } from './workday';

/** Every board adapter this extension ships, in match-check order. */
export const adapterRegistry: readonly BoardAdapter[] = [
  greenhouseAdapter,
  leverAdapter,
  ashbyAdapter,
  smartRecruitersAdapter,
  workdayAdapter
];

/**
 * Finds the board adapter whose host pattern matches the given URL's
 * hostname, if any. Zero tokens, zero DOM access — pure lookup, which is why
 * it's safe to call from the popup (to show "Adapter: Greenhouse") as well
 * as from the content script.
 */
export function matchAdapter(url: string | URL): BoardAdapter | undefined {
  let hostname: string;
  try {
    hostname = typeof url === 'string' ? new URL(url).hostname : url.hostname;
  } catch {
    return undefined;
  }
  return adapterRegistry.find((adapter) =>
    adapter.hostPatterns.some((pattern) => pattern.test(hostname))
  );
}
