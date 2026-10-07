import { createRequire } from 'node:module';
import { resolve, dirname, join } from 'node:path';
import { pathToFileURL } from 'node:url';

const [nodePath, entryPath, mode] = process.argv.slice(2);
if (!nodePath || !entryPath) throw new Error('Usage: node scripts/test-codex-mcp.mjs NODE SERVER_ENTRY [--revit]');
const entry = resolve(entryPath);
const requireServer = createRequire(pathToFileURL(join(dirname(dirname(entry)), 'package.json')));
const { Client } = await import(pathToFileURL(requireServer.resolve('@modelcontextprotocol/sdk/client/index.js')));
const { StdioClientTransport } = await import(pathToFileURL(requireServer.resolve('@modelcontextprotocol/sdk/client/stdio.js')));
const transport = new StdioClientTransport({ command: resolve(nodePath), args: [entry], stderr: 'pipe' });
const client = new Client({ name: 'codex-revit-setup-test', version: '1.0.0' });
try {
  await client.connect(transport);
  const tools = await client.listTools();
  for (const name of ['get_project_info', 'create_floor', 'get_warnings']) {
    if (!tools.tools.some(tool => tool.name === name)) throw new Error(`Expected tool missing: ${name}`);
  }
  console.log(JSON.stringify({ initialized: true, server: client.getServerVersion(), toolCount: tools.tools.length }));
  if (mode === '--revit') {
    const result = await client.callTool({ name: 'get_project_info', arguments: { compact: true } }, undefined, { timeout: 45000 });
    // Report connectivity without publishing model metadata or local paths.
    if (result.isError) throw new Error(`Revit read test failed: ${JSON.stringify(result.content)}`);
    console.log(JSON.stringify({ revitRead: 'passed', tool: 'get_project_info' }));
  }
} finally {
  await client.close();
}
