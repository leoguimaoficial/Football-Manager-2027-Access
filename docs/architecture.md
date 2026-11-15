# Architecture

FM27Access.sln has the same five projects as the original FM26 solution. There is
one UI implementation and one production HTTP server.

1. BepInEx loads FM27Access.Host once. Host owns Unity type injection, the frame
   dispatcher, native Prism, speech initialization and the companion process.
2. CoreHost loads FM27Access.Plugin.dll from bytes and calls CoreEntry.Mount through
   IHostBridge. Host holds no compile-time reference to Plugin.
3. Plugin integrates native FM27 panels and actions with the original walker,
   adapters, profiles, navigation, HTML renderer, server handles and dropdown sessions.
4. Navigation and Speech retain the mature shared models, builders and pipeline.
5. The standalone WebView2 companion reads the loopback HTML server and SSE updates.

POST /reload unmounts and replaces Plugin on the Unity thread. The permanent host
keeps the session token, logging and pump alive. Navigation, Speech, Prism and Host
are not hot reloadable. Native calls never run on server worker threads.

tools/game-inspect is a standalone build-time utility with no runtime dependency.
Tests link selected pure production sources to run without proprietary assemblies.
The companion shell is built separately, as in FM26. Sounds, Prism, lib and
packaging retain redistributable dependencies and their notices.

Build and install are separate. Receipt-managed scripts protect existing files and
record hashes. Local SDKs, upstream comparison sources, evidence, backups, receipts,
build outputs and test reports are ignored. No proprietary game data is packaged.
