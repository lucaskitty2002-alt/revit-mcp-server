// Dependency-free package smoke test: initialization/listing only unless --revit is explicit.
import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { createInterface } from 'node:readline';

const [nodePath, entryPath, mode] = process.argv.slice(2);
if (!nodePath || !entryPath) throw new Error('Usage: node scripts/test-codex-mcp.mjs NODE SERVER_ENTRY [--revit]');
const child = spawn(resolve(nodePath), [resolve(entryPath)], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
const pending = new Map();
let nextId = 1;
function fail(error) {
  for (const { reject, timer } of pending.values()) { clearTimeout(timer); reject(error); }
  pending.clear();
}
child.on('error', fail);
child.on('exit', () => fail(new Error('MCP test process exited before responding.')));
child.stderr.on('data', () => {}); // Drain diagnostics without publishing model metadata/paths.
const lines = createInterface({ input: child.stdout });
lines.on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  const waiter = pending.get(message.id);
  if (!waiter) return;
  pending.delete(message.id); clearTimeout(waiter.timer);
  if (message.error) waiter.reject(new Error('MCP protocol error: ' + message.error.message));
  else waiter.resolve(message.result);
});
function request(method, params = {}, timeoutMs = 15000) {
  return new Promise((resolveResult, reject) => {
    const id = nextId++;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('MCP timed out: ' + method)); }, timeoutMs);
    pending.set(id, { resolve: resolveResult, reject, timer });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n', error => {
      if (error) { clearTimeout(timer); pending.delete(id); reject(error); }
    });
  });
}
try {
  const initialized = await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {}, clientInfo: { name: 'vue-studio-package-test', version: '2.7.0' }
  });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const { tools } = await request('tools/list');
  for (const name of ['get_project_info', 'create_floor', 'get_warnings']) {
    if (!tools?.some(tool => tool.name === name)) throw new Error('Expected tool missing: ' + name);
  }
  console.log(JSON.stringify({ initialized: true, server: initialized.serverInfo, toolCount: tools.length }));
  if (mode === '--revit') {
    const result = await request('tools/call', { name: 'get_project_info', arguments: { compact: true } }, 45000);
    if (result.isError) throw new Error('Revit read test failed; inspect the local MCP log.');
    console.log(JSON.stringify({ revitRead: 'passed', tool: 'get_project_info' }));
  }
} finally {
  fail(new Error('MCP test closed.'));
  lines.close(); child.stdin.end();
  const timer = setTimeout(() => { if (child.exitCode === null) child.kill(); }, 2000);
  timer.unref();
}
