// Hook I/O (guardrails.spec Contract): read stdin JSON; deny or block with exit 2 and a reason on
// stderr; every hook fails closed by routing its own errors through deny/block.
import { readFileSync } from 'node:fs';

export function readInput() {
  const input = JSON.parse(readFileSync(0, 'utf8'));
  if (typeof input !== 'object' || input === null) throw new Error('hook input is not an object');
  return input;
}

export function deny(reason) {
  process.stderr.write(`${reason}\n`);
  process.exit(2);
}

export function allow() {
  process.exit(0);
}

// Runs a hook body; any thrown error denies (fails closed) with the hook's name in the reason.
export function guarded(name, body) {
  try {
    body();
  } catch (err) {
    deny(`${name}: internal error, denied to fail closed (${err.message})`);
  }
  allow();
}

// F0a spike rulings: agent_id marks a subagent; agent_type is its role (may be empty).
export function identity(input) {
  const subagent = typeof input.agent_id === 'string' && input.agent_id !== '';
  const role = subagent && typeof input.agent_type === 'string' ? input.agent_type : '';
  return { subagent, role, agentId: subagent ? input.agent_id : null };
}
