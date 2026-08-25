# Compatibility architecture

Version 2 treats the stable game API as the compile-time baseline and resolves known channel differences inside `Compatibility/`. The shipped assembly must not directly reference a member whose signature or enum value differs between stable and beta.

## Compatibility boundary

`GameCompatibility` owns constructor, property and method-shape differences. `RuntimeNetErrors` owns enum values that keep their names but not their numeric representation. `PatchBootstrap` applies Harmony patch classes independently so an optional UI or gameplay patch can be disabled without taking down the LAN transport.

Lobby serialization stays vanilla. The game currently represents four lobby slots safely; raising the limit would require a protocol extension negotiated by both peers, not an unconditional serializer patch.

LAN discovery is a separate, versioned UDP protocol on port `33770`. It only advertises lobby metadata and never carries game state. Unknown protocol versions and malformed datagrams must be ignored without affecting the ENet session. Latency and loss values measure the discovery round trip before joining; gameplay synchronization remains owned by the game.

## Verifying a game update

1. Copy the stable and beta game reference assemblies into separate directories. Each directory must contain `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll`.
2. Run `scripts/verify-dual-version.ps1` with both directories.
3. Compare the decompiled mod assemblies. Differences caused by a direct game API reference must be moved behind `Compatibility/`.
4. Test host, join, cancel, timeout, reconnect, new run and load-run flows in the game on both channels.
5. Check the log for disabled patch groups. A skipped cosmetic group is degradable; a skipped ENet, host-menu or join-menu group blocks the corresponding LAN feature.

## Rules for future changes

* Prefer public interfaces shared by both channels over concrete multiplayer services.
* Resolve constructors and renamed properties by reflection once, then cache the result.
* Parse game enums by name when their numeric layout is not guaranteed.
* Locate scene nodes recursively and fail closed when a required node is absent.
* Keep UI patches idempotent and run them after vanilla initialization.
* Do not patch multiplayer serialization unless the protocol includes explicit version negotiation.
* Do not put game DLLs in the repository.

Successful compilation is necessary but not sufficient. A real two-process game session is still required before publishing a release.
