# Game installation inspector

A game-independent command-line utility for Xbox/Steam identity discovery, PE
architecture, Unity version, metadata version and binary fingerprints. It never
loads game assemblies or modifies the installation.

    dotnet run --project tools/game-inspect -- inspect '<game root>'
    dotnet run --project tools/game-inspect -- discover-steam '<Steam root>'

InstallationInspector.cs is source-linked into the unit tests. This utility is not
part of the BepInEx runtime and does not serve a parallel companion interface.
