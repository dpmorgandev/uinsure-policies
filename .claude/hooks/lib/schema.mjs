// A minimal JSON Schema subset validator (no npm dependencies), enough for orchestration.spec's verdict
// schema v1: type, required, properties, additionalProperties:false, enum, const, pattern, minimum,
// maximum, minItems, maxLength, items. The schema itself is read from the verdict-format skill, so the
// hook and the skill can't drift apart.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const SKILL = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'skills', 'verdict-format', 'SKILL.md');

export function verdictSchema() {
  const text = readFileSync(SKILL, 'utf8');
  const block = /```json\r?\n([\s\S]*?)\r?\n```/.exec(text);
  if (!block) throw new Error('verdict schema block not found in the verdict-format skill');
  return JSON.parse(block[1]);
}

function typeOf(v) {
  if (Array.isArray(v)) return 'array';
  if (v === null) return 'null';
  if (Number.isInteger(v)) return 'integer';
  return typeof v;
}

export function validate(schema, value, path = '$') {
  const errors = [];
  const t = typeOf(value);
  if (schema.type && !(schema.type === t || (schema.type === 'number' && t === 'integer'))) {
    return [`${path}: expected ${schema.type}, got ${t}`];
  }
  if ('const' in schema && value !== schema.const) errors.push(`${path}: must be ${JSON.stringify(schema.const)}`);
  if (schema.enum && !schema.enum.includes(value)) errors.push(`${path}: must be one of ${schema.enum.join('|')}`);
  if (schema.pattern && (typeof value !== 'string' || !new RegExp(schema.pattern).test(value))) errors.push(`${path}: must match ${schema.pattern}`);
  if (typeof schema.minimum === 'number' && typeof value === 'number' && value < schema.minimum) errors.push(`${path}: below ${schema.minimum}`);
  if (typeof schema.maximum === 'number' && typeof value === 'number' && value > schema.maximum) errors.push(`${path}: above ${schema.maximum}`);
  if (typeof schema.maxLength === 'number' && typeof value === 'string' && value.length > schema.maxLength) errors.push(`${path}: longer than ${schema.maxLength}`);
  if (t === 'array') {
    if (typeof schema.minItems === 'number' && value.length < schema.minItems) errors.push(`${path}: needs at least ${schema.minItems} item(s)`);
    if (schema.items) value.forEach((item, i) => errors.push(...validate(schema.items, item, `${path}[${i}]`)));
  }
  if (t === 'object' && (schema.properties || schema.required)) {
    for (const key of schema.required ?? []) if (!(key in value)) errors.push(`${path}: missing ${key}`);
    for (const [key, v] of Object.entries(value)) {
      if (schema.properties?.[key]) errors.push(...validate(schema.properties[key], v, `${path}.${key}`));
      else if (schema.additionalProperties === false) errors.push(`${path}: unexpected field ${key}`);
    }
  }
  return errors;
}

// The final message must be exactly one JSON object; a single ```json fence around it is tolerated.
export function parseFinalMessage(message) {
  const text = String(message ?? '').trim();
  const fenced = /^```(?:json)?\r?\n([\s\S]*?)\r?\n```$/.exec(text);
  const value = JSON.parse(fenced ? fenced[1] : text);
  if (typeOf(value) !== 'object') throw new Error('the final message is not a JSON object');
  return value;
}
