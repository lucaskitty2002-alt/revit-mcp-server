# Security policy

## Supported code

Security fixes are developed on the current `main` branch of this repository.
Update to a reviewed release or reviewed commit containing a fix. Older builds are
not assumed to receive backported fixes. The project does not claim certification
or guarantee that all vulnerabilities have been found.

## Report a vulnerability privately

Use [Report a vulnerability](https://github.com/lucaskitty2002-alt/revit-mcp-server/security/advisories/new).
Include the affected commit or release, Revit and Windows versions, impact,
reproduction steps, and a minimal example using a disposable test model.

Do not post credentials, project models, personal data, or exploit details in
public issues or pull requests. Remove identifying information from logs.
Maintainers will assess reports and coordinate disclosure; no response or repair
time is guaranteed.

## Revit MCP trust boundary

The MCP client and tools can read and change models and may execute code with
the Windows permissions of the Revit user. Only connect trusted clients and
review prompts, tools, code, downloads, and model changes before use.

The Revit TCP bridge is intended for the same computer. A loopback listener is
not authentication and does not isolate untrusted local processes or other
users. Do not expose, forward, tunnel, or proxy its ports to a network or the
internet. Do not use an untrusted shared computer. Stop the bridge when unused.

Use a standard Windows account with only the necessary file access, separate
test models and backups, and explicit human review before changing production
models. Keep secrets outside Git and avoid confidential data in prompts/logs.
Rotate any exposed credential at its provider; deleting it from a file is
insufficient because Git history may retain it.

## Development and releases

Changes to the default branch must follow the repository's pull-request and
security-check rules. Review Dependabot pull requests and scanner findings;
a successful scan is not proof that a change is safe. Do not automatically merge
dependency changes without review.

Use pinned action commit SHAs and minimal workflow permissions. Retain the
loopback binding. Changes involving remote access, authentication, arbitrary code
execution, command permissions, logging, or installers need explicit security
review. GitHub protections do not replace testing the installed Revit bridge.
