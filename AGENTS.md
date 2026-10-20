# Shared GitHub workflow

This is a shared project for Witte and Lucas. The user requires completed project changes to be saved on GitHub so both collaborators and their chats can see the work.

- Fetch the latest remote state before publishing, and preserve other collaborators' changes.
- Commit and push completed source changes to a descriptive branch, then provide the GitHub branch or pull request link in the final response.
- Do not leave completed changes only on the local computer. If authentication or permissions block upload, state the exact blocker and help resolve it.
- Use the intended collaborator's own GitHub account. Witte uses `wittenhoff1`; Lucas uses his own account. Never share credentials.
- Do not force-push, overwrite another collaborator's work, or merge without authorization.
- Keep Revit models, credentials, personal configuration, local databases, backups, and build caches out of Git. Share installation binaries through release assets when requested.

For Codex chat changes, build the Revit 2026 plugin and run the relevant checks described in README.md before publishing.
