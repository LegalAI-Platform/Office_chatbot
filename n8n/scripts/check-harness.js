// Reads the WA-TEST-HARNESS result (JSON on stdin) and prints PASS/FAIL per check.
'use strict';
let input = '';
process.stdin.on('data', (d) => { input += d; });
process.stdin.on('end', () => {
  let r;
  try { r = JSON.parse(input); } catch { console.log('FAIL  harness produced no JSON'); process.exit(1); }
  const checks = [
    ['n8n: event processed through META-WA-PROCESS-EVENT', r.first_process && r.first_process.messages_new === 1],
    ['n8n: same event again is a duplicate', r.second_process && r.second_process.duplicate_event === true],
    ['n8n: text after 25h refused with OUTSIDE_24H_WINDOW', r.send_text_after_25h && r.send_text_after_25h.error_code === 'OUTSIDE_24H_WINDOW'],
    ['n8n: unapproved template refused with TEMPLATE_NOT_APPROVED', r.send_unapproved_template && r.send_unapproved_template.error_code === 'TEMPLATE_NOT_APPROVED'],
    ['n8n: invalid input refused with INVALID_INPUT', r.send_invalid_input && r.send_invalid_input.error_code === 'INVALID_INPUT'],
    ['n8n: refused messages never reached Meta (attempts = 0)', r.database && r.database.outbound
      && Object.values(r.database.outbound).every((o) => o.attempts === 0)],
    ['n8n: exactly one inbound row and one event row', r.database && r.database.inbound_rows === 1 && r.database.event_rows === 1],
  ];
  let failed = 0;
  for (const [name, ok] of checks) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
    if (!ok) failed += 1;
  }
  process.exit(failed ? 1 : 0);
});
