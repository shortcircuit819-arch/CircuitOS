CIRCUITOS UPDATE
================

This archive updates CircuitOS application files without containing or replacing
the Data folder.

1. Close CircuitOS.
2. Back up the installed CircuitOS Data folder.
3. Open this update ZIP and copy all files and folders into the existing
   CircuitOS installation folder.
4. Allow Windows to replace files with matching names.
5. Double-click CircuitOS.exe and confirm the displayed version.

The update contains CircuitOS.exe, App, Overlay, Streamerbot Actions, Documentation,
and version.json. It intentionally contains no Data directory.

After an update, open Profile Settings, then Streamer.bot Setup. Regenerate the
Streamer.bot C# actions when release notes mention action-template, command,
message, path, or integration changes. Template fixes may require regeneration
even when the displayed integration version is unchanged.
