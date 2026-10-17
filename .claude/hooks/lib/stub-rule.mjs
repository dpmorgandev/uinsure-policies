// Tester src/ stub rule (testing.spec C7 grammar; guardrails.spec G2 mechanism): a tester change under
// src/ may only add lines, and every added line, trimmed, must match the grammar below.
export const GRAMMAR = [
  /^$/,
  /^[{}];?$/,
  /^using [\w.]+;$/,
  /^namespace [\w.]+;$/,
  /^(public|internal)( (sealed|static|abstract|partial))* (class|record|struct|interface|enum) \w+(<[^>]+>)?(\(.*\))?( : [\w.<>, ]+)?;?$/,
  /^(public|internal|protected)( (static|virtual|override|abstract|async))* [\w.<>?\[\], ]+ \w+(<[^>]+>)?\([^)]*\)( => throw new NotImplementedException\(\);)?$/,
  /^(public|internal) [\w.<>?\[\], ]+ \w+ \{ get; (init; |set; )?\}$/,
  /^\w+,?$/,
  /^throw new NotImplementedException\(\);$/,
];

export function isStubLine(line) {
  const t = line.trim();
  return GRAMMAR.some((re) => re.test(t));
}

function lines(text) {
  return (text ?? '').replace(/\r\n/g, '\n').split('\n');
}

// Edit old_string → new_string: every old line must survive, in order (nothing removed or modified),
// and every added line must be a stub line. Returns the offending lines (empty = allowed).
export function checkEdit(oldText, newText) {
  const oldLines = lines(oldText);
  const newLines = lines(newText);
  const bad = [];
  let i = 0;
  for (const line of newLines) {
    if (i < oldLines.length && line === oldLines[i]) i++;
    else if (!isStubLine(line)) bad.push(`added: ${line.trim()}`);
  }
  if (i < oldLines.length) bad.push(`removed or modified: ${oldLines[i].trim()}`);
  return bad;
}

// A unified diff with -U0: any removed line, or an added line outside the grammar, is a violation.
export function checkDiff(diff) {
  const bad = [];
  for (const line of lines(diff)) {
    if (line.startsWith('+++') || line.startsWith('---')) continue;
    if (line.startsWith('-')) bad.push(`removed: ${line.slice(1).trim()}`);
    else if (line.startsWith('+') && !isStubLine(line.slice(1))) bad.push(`added: ${line.slice(1).trim()}`);
  }
  return bad;
}
