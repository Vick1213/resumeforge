import { parse as parseYaml } from 'yaml';
import type { KnowledgeItemKind } from '@/api/types';

export interface SplitMarkdownResult {
  frontmatter: Record<string, unknown>;
  body: string;
  error?: string;
}

const FRONTMATTER_PATTERN = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)$/;

/** Splits a knowledge-base markdown file into its YAML frontmatter and body. */
export function splitMarkdown(raw: string): SplitMarkdownResult {
  const match = FRONTMATTER_PATTERN.exec(raw);
  if (!match) {
    return { frontmatter: {}, body: raw, error: 'missing frontmatter fences (---)' };
  }

  const yamlBlock = match[1] ?? '';
  const body = match[2] ?? '';

  try {
    const parsed = parseYaml(yamlBlock) as unknown;
    if (parsed === null || parsed === undefined) {
      return { frontmatter: {}, body: body.replace(/^\r?\n/, '') };
    }
    if (typeof parsed !== 'object' || Array.isArray(parsed)) {
      return { frontmatter: {}, body: body.replace(/^\r?\n/, ''), error: 'frontmatter must be a YAML mapping' };
    }
    return { frontmatter: parsed as Record<string, unknown>, body: body.replace(/^\r?\n/, '') };
  } catch (err) {
    return { frontmatter: {}, body: body.replace(/^\r?\n/, ''), error: err instanceof Error ? err.message : 'invalid YAML' };
  }
}

export function joinMarkdown(frontmatter: string, body: string): string {
  return `---\n${frontmatter}\n---\n\n${body.trimStart()}`;
}

/** A blank starting template for a new knowledge item of the given kind. */
export function starterMarkdown(kind: KnowledgeItemKind): string {
  switch (kind) {
    case 'experience':
      return '---\ntype: experience\nrole: \norganization: \nlocation: \nstartDate: \ntech: []\ntags: []\n---\n\n- \n';
    case 'project':
      return '---\ntype: project\nname: \ntagline: \nurl: \nrepoUrl: \ntech: []\ntags: []\nsource: manual\n---\n\n- \n';
    case 'education':
      return '---\ntype: education\ninstitution: \ncredential: \nstartDate: \nendDate: \n---\n\n';
    case 'certification':
      return '---\ntype: certification\nname: \nissuer: \nissuedOn: \n---\n\n';
    case 'basics':
      return '---\ntype: basics\nfullName: \nheadline: \nemail: \n---\n\n';
  }
}

export interface ParsedBullet {
  text: string;
  variants: string[];
}

/**
 * Parses the KB body format from CONTRACTS §3: top-level `-` items are
 * bullets, an indented `-` beneath a bullet is a variant, and any other
 * indented line continues whichever bullet or variant came before it
 * (soft-wrapped markdown).
 */
export function parseBullets(body: string): ParsedBullet[] {
  const lines = body.split(/\r?\n/);
  const bullets: ParsedBullet[] = [];
  let continuation: 'text' | number | null = null;

  for (const rawLine of lines) {
    if (rawLine.trim().length === 0) {
      continuation = null;
      continue;
    }

    const indent = (/^(\s*)/.exec(rawLine)?.[1] ?? '').length;
    const trimmed = rawLine.trim();
    const current = bullets[bullets.length - 1];

    if (indent === 0 && trimmed.startsWith('- ')) {
      bullets.push({ text: trimmed.slice(2).trim(), variants: [] });
      continuation = 'text';
      continue;
    }

    if (indent > 0 && trimmed.startsWith('- ')) {
      if (!current) continue;
      current.variants.push(trimmed.slice(2).trim());
      continuation = current.variants.length - 1;
      continue;
    }

    if (!current) continue;
    if (continuation === 'text') {
      current.text = `${current.text} ${trimmed}`;
    } else if (typeof continuation === 'number') {
      const existing = current.variants[continuation];
      if (existing !== undefined) {
        current.variants[continuation] = `${existing} ${trimmed}`;
      }
    }
  }

  return bullets;
}

export interface FrontmatterIssue {
  field: string;
  message: string;
  severity: 'error' | 'warning';
}

export interface FrontmatterValidationResult {
  issues: FrontmatterIssue[];
  isValid: boolean;
}

type FieldValidator = (value: unknown) => string | null;

interface FieldRule {
  key: string;
  required: boolean;
  validate: FieldValidator;
}

function isValidDateToken(value: unknown, allowPresent: boolean): boolean {
  if (typeof value !== 'string') return false;
  if (value === 'present') return allowPresent;
  return /^\d{4}(-\d{2}(-\d{2})?)?$/.test(value);
}

function stringRule(): FieldValidator {
  return (v) => (typeof v === 'string' && v.trim().length > 0 ? null : 'must be a non-empty string');
}

function dateRule(allowPresent: boolean): FieldValidator {
  return (v) => (isValidDateToken(v, allowPresent) ? null : 'must be "yyyy-MM", "yyyy-MM-dd", "yyyy", or "present"');
}

function stringArrayRule(): FieldValidator {
  return (v) => (Array.isArray(v) && v.every((item) => typeof item === 'string') ? null : 'must be an array of strings');
}

function numberRule(): FieldValidator {
  return (v) => (typeof v === 'number' && Number.isFinite(v) ? null : 'must be a number');
}

const RULES_BY_TYPE: Record<string, FieldRule[]> = {
  experience: [
    { key: 'role', required: true, validate: stringRule() },
    { key: 'organization', required: true, validate: stringRule() },
    { key: 'location', required: false, validate: stringRule() },
    { key: 'startDate', required: true, validate: dateRule(false) },
    { key: 'endDate', required: false, validate: dateRule(true) },
    { key: 'tech', required: false, validate: stringArrayRule() },
    { key: 'tags', required: false, validate: stringArrayRule() },
  ],
  project: [
    { key: 'name', required: true, validate: stringRule() },
    { key: 'tagline', required: false, validate: stringRule() },
    { key: 'url', required: false, validate: stringRule() },
    { key: 'repoUrl', required: false, validate: stringRule() },
    { key: 'startDate', required: false, validate: dateRule(true) },
    { key: 'endDate', required: false, validate: dateRule(true) },
    { key: 'tech', required: false, validate: stringArrayRule() },
    { key: 'tags', required: false, validate: stringArrayRule() },
    {
      key: 'source',
      required: false,
      validate: (v) => (v === 'manual' || v === 'github' ? null : 'must be "manual" or "github"'),
    },
    { key: 'stars', required: false, validate: numberRule() },
  ],
  education: [
    { key: 'institution', required: true, validate: stringRule() },
    { key: 'credential', required: true, validate: stringRule() },
    { key: 'location', required: false, validate: stringRule() },
    { key: 'startDate', required: false, validate: dateRule(true) },
    { key: 'endDate', required: false, validate: dateRule(true) },
    { key: 'gpa', required: false, validate: numberRule() },
    { key: 'tags', required: false, validate: stringArrayRule() },
  ],
  certification: [
    { key: 'name', required: true, validate: stringRule() },
    { key: 'issuer', required: false, validate: stringRule() },
    { key: 'issuedOn', required: false, validate: dateRule(false) },
    { key: 'credentialUrl', required: false, validate: stringRule() },
  ],
  basics: [
    { key: 'fullName', required: true, validate: stringRule() },
    { key: 'headline', required: false, validate: stringRule() },
    { key: 'email', required: false, validate: stringRule() },
    { key: 'phone', required: false, validate: stringRule() },
    { key: 'location', required: false, validate: stringRule() },
    { key: 'website', required: false, validate: stringRule() },
    { key: 'linkedIn', required: false, validate: stringRule() },
    { key: 'gitHub', required: false, validate: stringRule() },
  ],
};

/**
 * Validates parsed frontmatter against the rules in CONTRACTS §3. Unknown
 * keys are never an error -- the parser contract preserves them verbatim --
 * only missing required fields and malformed known fields are flagged.
 */
export function validateFrontmatter(frontmatter: Record<string, unknown>): FrontmatterValidationResult {
  const issues: FrontmatterIssue[] = [];
  const rawType = frontmatter['type'];
  const type = typeof rawType === 'string' ? rawType : undefined;

  if (!type) {
    issues.push({ field: 'type', message: '"type" is required', severity: 'error' });
    return { issues, isValid: false };
  }

  const rules = RULES_BY_TYPE[type];
  if (!rules) {
    issues.push({
      field: 'type',
      message: `unknown type "${type}" (expected experience, project, education, certification, or basics)`,
      severity: 'error',
    });
    return { issues, isValid: false };
  }

  for (const rule of rules) {
    const value = frontmatter[rule.key];
    if (value === undefined) {
      if (rule.required) {
        issues.push({ field: rule.key, message: `"${rule.key}" is required`, severity: 'error' });
      }
      continue;
    }
    const error = rule.validate(value);
    if (error) {
      issues.push({ field: rule.key, message: `"${rule.key}" ${error}`, severity: 'error' });
    }
  }

  return { issues, isValid: !issues.some((issue) => issue.severity === 'error') };
}
