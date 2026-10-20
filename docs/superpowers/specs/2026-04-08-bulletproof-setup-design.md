# Installation and connectivity design

The setup design is maintained in the current [README](../../../README.md) and [Codex setup](../../codex-setup-da.md).

The installer validates a complete package before replacement, backs up the existing add-in, preserves other plugins, and registers the external server using the Codex CLI. Revit startup does not edit another application's configuration.

The panel discovers Codex, uses its existing login through app-server, supplies Revit tools dynamically, and checks the plugin's actual TCP port. Diagnostics cover plugin dependencies, Codex availability, and local TCP connectivity. Structured plugin logging and port discovery remain in place.
