# Absolute Chaos — Multiplayer Ragdoll Arena

A fast-paced 3-player ragdoll stick figure brawler built in Unity. Players compete across rounds in physics-driven arenas, picking up guns, throwing them, and punching each other off the stage. A card draft between rounds lets survivors stack passive upgrades. First to 5 round wins takes the match.

[GitHub Repository](https://github.com/BlueRobotMiner/AbsoluteChaos)

---

## Known Issues

### Active (Unresolved)
- **Bullet visuals linger on Player 2's screen** — depending on the gun used, visual bullets stick at their impact position for a few seconds instead of disappearing instantly; the 8-second fallback removes them but the root cause (certain gun hit paths not triggering `NotifyHitClientRpc` in time) is under investigation
- **Player 1 draft movement can loop endlessly** — sometimes the drafter's character walks left or right and does not stop under the mouse cursor; believed to be a rect-space mismatch between the card UI canvas and the world camera at certain resolutions
- **Arms clip through countdown overlay** — when the round-start countdown panel fades in, ragdoll arms and hands can visually poke through above the UI panel before physics settles; cosmetic only
- **ExplosiveRounds card animation non-functional** — the `BroadcastExplosion` RPC now fires correctly and the `ExplosionEffect` component exists, but the explosion circle animation does not play on impact; requires the explosion child GO to be properly assigned in the bullet prefab inspector
- **Gun does not despawn on rematch** — guns despawn correctly at match end, but persist through a rematch vote back into CardDraft
- **Three-player testing incomplete** — the game is designed for 3 players but has only been regularly tested with 2; edge cases around 3-player draft order and elimination flow may surface
- **Right-click active card** — `RMB` active card activation is stubbed; no card has an active effect yet

### Resolved
- ~~Card effects not implemented~~ — **FIXED** — all passive card effects apply each round via `MapManager.ApplyCardForSlot` (SpeedBoost, DoubleJump, RapidFire, Ricochet, ExplosiveRounds, AmmoStash, HealthPackRain, LowGravity, HeavyGravity, Fragile)
- ~~Player cards passive effects unimplemented~~ — **FIXED** — see above
- ~~No pause menu~~ — **FIXED** — pause menu implemented
- ~~Relay requires a full build~~ — **FIXED** — Unity Relay confirmed working in-editor
- ~~Gun slightly offset on Player 2 while holder moves quickly~~ — **FIXED**

---

## Setup

### Prerequisites
- Unity 2022.3.62f3 (LTS)
- Git

### Installation
```bash
git clone https://github.com/BlueRobotMiner/AbsoluteChaos.git
```
1. Open the project in Unity Hub
2. Open `Assets/Scenes/MainMenu.unity`
3. Press Play to test in the editor

---

## How to Play

### Controls
| Input | Action |
|-------|--------|
| A / D | Move left / right |
| Space | Jump |
| Mouse | Aim arm |
| Left Click | Shoot (with gun) / Punch (no gun) |
| Right Click | Active card (stub — not yet implemented) |
| G | Throw held gun |

### Objective
Eliminate all other players to win the round. First player to **5 round wins** takes the match.

---

## Multiplayer Setup

### Local Network (LAN)
1. One machine clicks **Host → Create Local** — your LAN IP is shown in the lobby
2. The other machine clicks **Join → Join Local** — the IP field auto-fills; hit Submit

### Online (Unity Relay)
1. Host clicks **Host → Create Public** — a 6-character join code appears top-left of the lobby
2. The other player enters that code under **Join → Submit**
> Note: Relay requires a full build to work reliably. Running both instances in the Editor may not connect.

---

## Scene Flow
```
MainMenu → Lobby (playable arena, waiting room)
        → CardDraft (all players pick 1 card — first draft only)
              → Map1 → CardDraft (losers pick)
              → Map2 → CardDraft (losers pick)
              → Map3 → CardDraft (losers pick)
              → Map1 → ... (rotation shuffles, continues until 5 wins)
                    → Results (match winner, rematch vote)
```

---

## Technical Implementation

| Pattern | File | Notes |
|---------|------|-------|
| **Singleton** | `GameManager.cs`, `AudioManager.cs` | Persist across scenes via DontDestroyOnLoad |
| **Object Pool** | `ProjectilePool.cs` | Queue-based bullet pool, overflow-safe |
| **Delegate Events** | `GameManager.cs` | `OnRoundEnd`, `OnMatchEnd`, `OnPlayerEliminated` |
| **Server Authority** | `PlayerController.cs`, `Gun.cs` | Server runs all physics; clients are pure renderers |

---

## Bug Fixes (This Build)

### Networking / Multiplayer
- **Player 2 couldn't move after dying** — `SetInputEnabled` was server-only; fixed with `SetInputEnabledClientRpc` targeting the owner
- **Player 2 couldn't shoot after respawn** — same pattern; added `SetShootingEnabledClientRpc`
- **HUD didn't sync to Player 2** — round/match events only fired on the server; added `SyncRoundEndClientRpc` / `SyncMatchEndClientRpc` to push scores and fire delegate events on clients
- **Health bars not re-enabled on map load** — server called `SetHealthBarVisible` locally only; fixed with `ShowHealthBarsClientRpc`
- **Health bar text stale on spawn** — `NetworkVariable.OnValueChanged` doesn't fire when value is unchanged; force-refresh `UpdateHealthUI` when the bar becomes visible
- **Card stacks never synced to Player 2** — `GameManager.PlayerCardStacks` is a plain list, server-only; added `SyncCardPickClientRpc` so every confirmed pick propagates to all clients
- **Card icons and names blank on Player 2's HUD** — consequence of empty stacks above; resolved by the sync fix plus explicitly setting `nameLabel.text` in `CardHUD.Refresh()`
- **Player not spawning after draft scene** — player GameObjects set `SetActive(false)` during draft carry that state into the next map; added `ReactivateAllPlayersClientRpc` in `MapManager.RespawnAfterLoad` to re-enable all player GOs before repositioning
- **Player 2 character not moving on draft screen** — `SetInputEnabledClientRpc(false)` disabled the server-side physics gate for P2; `EnableDraftAfterDelay` re-enabled input locally but never unblocked the server's `HandleMove`; added `SetInputEnabledServerRpc(true)` call in `CardDraftingUI`

### Gun
- **Gun throw not visible on Player 2** — `NetworkTransform` was re-enabled on the server but not on clients after throw; fixed in `ClearHolderClientRpc`
- **Gun immediately re-picked up after throw** — colliders re-enabled while still overlapping the thrower caused instant `OnTriggerEnter2D`; added `_pickupCooldownEnd` (0.5 s grace period)
- **Gun zipping on Player 2 screen during throw** — NT's interpolation buffer was stale (disabled during entire hold period); fixed by having the throwing client send their local gun position in `ThrowWeaponServerRpc`; server snaps to that position before launching, then broadcasts it to all clients via `ClearHolderClientRpc` so NT starts from the correct origin
- **Gun floating / lagging behind hand on Player 2** — server-driven NT had inherent interpolation delay; reverted to each client independently reading the local hand transform while held (NT disabled during hold, re-enabled on throw)
- **Gun collider resized by spin effect** — spin code was modifying `sr.transform.localScale` which, when the SpriteRenderer is on the root GO, also scales the physics colliders; replaced with `flipX` toggling (pure visual, zero physics impact)
- **Gun spun like a wheel (Z-axis)** — changed to a Y-axis coin-flip effect using `SpriteRenderer.flipX` driven by `Mathf.Sin`
- **Collider confusion after swap** — BoxCollider2D is now the pickup trigger, CircleCollider2D is the solid landing collider; `IgnorePlayerCollisions` finds the solid by `!isTrigger` so no code changes were needed

### Movement / Physics
- **Jump broken while walking** — `rb.MovePosition()` overwrote the Y component every frame, killing gravity; replaced with `rb.velocity = new Vector2(h * speed, rb.velocity.y)`
- **Knockback immediately cancelled by movement** — `HandleMove` overwrote `velocity.x` every FixedUpdate; added `_suppressHorizontal` flag and `KnockbackSuppressCoroutine`
- **Killbox pushed players in the wrong direction** — direction was relative to the killbox position which caused side boxes to push vertically; replaced with a live `(mapCenter − player).normalized` direction so every killbox always pushes toward the map center

### Card Draft
- **NullRef in `HandleDraftInput`** — `SetDraftMode(false, null, null)` nulled `_draftManager` before the reference was used; saved to a local variable first
- **Player character flashing during drafter transition** — `InitializePositionClientRpc` (on the player's NetworkObject) and `DealCardsClientRpc` (on CardDraftingUI) are different objects so NGO gives no ordering guarantee; fixed by hiding all players immediately, then showing only the drafter after one frame
- **Dead players appearing during last drafter's pick** — `ShowAllPlayersClientRpc` was called before scene transition and re-enabled dead player GOs; removed the call; map load now re-enables players via `ReactivateAllPlayersClientRpc`

### Projectiles
- **Bullet lifetime not tied to impact** — bullets previously despawned on a timer regardless of whether they hit anything; removed lifetime logic entirely; bullets now only despawn on surface impact
- **Ricochet stackable** — `SetRicochet(bool)` replaced with `AddRicochetBounces(int)`; each Ricochet card adds 3 bounces that stack additively
- **Explosive bullet despawned before animation** — `BroadcastExplosion` was gated behind a null check on `_explosionEffect`; if the field was unassigned the bullet fell through to `BroadcastHit` and was immediately removed; `BroadcastExplosion` now always fires when `_explosive` is true

### UI / Audio
- **Rematch sent players to Lobby instead of CardDraft** — `StartRematch()` was loading `"Lobby"`; corrected to `"CardDraft"`
- **Round progress bars showed text labels** — replaced with color-coded sliders tinted to each player's slot color
- **Music never changed from main menu track** — `AudioManager` now subscribes to `SceneManager.sceneLoaded` and auto-switches: menu/lobby → menu music, CardDraft → draft music, maps → game music
- **Tooltip canvas visible at scene start** — canvas panel must be set **inactive** in the Unity hierarchy; `CardHUD` calls `SetActive(false)` in both `Awake` and `Start` but can't prevent a one-frame flash if the designer leaves it active in the scene
- **Countdown SFX never played** — `ShowCountdownClientRpc` only updated text; added a dedicated `PlayCountdownSFXClientRpc` fired once at countdown start so all clients hear the audio clip
- **Punch playing hit SFX instead of punch SFX** — `PlayerHealth.OnHealthChanged` played `PlayHitSFX` for all damage sources; removed it; bullet hits now play `PlayHitSFX` via `NotifyHitClientRpc` (all clients), punch hits play `PlayPunchSFX` via `PlayPunchSFXClientRpc` broadcast when `PunchServerRpc` connects
- **Health pack playing hit SFX** — `HealthPack.cs` was calling `PlayHitSFX`; corrected to `PlayHealthPickupSFX`
- **Bullet visuals lingering on Player 2 screen** — trigger colliders were left enabled on client-side visual bullets generating unnecessary callbacks; disabled all colliders on visual bullets; added an 8-second fallback lifetime in `BulletVisual.Update` to clean up any orphaned visuals the server hit RPC never reached (e.g. bullets that exit the map)

---

## Features

### Card System
- 10 cards implemented with real gameplay effects
- Draft screen: the drafter's ragdoll physically walks toward the hovered card; hovering triggers card scale-up and audio feedback
- Cards that modify the global environment (LowGravity, HeavyGravity) are de-prioritized in the offer pool when another player already owns them
- Stacking supported: owning a card twice multiplies its effect (e.g. two RapidFire cards = 0.25× fire rate)

### Character Customization
- Players can set a display name, choose a head shape, and pick a body color before entering the lobby
- Preferences saved to `Application.persistentDataPath/playersave.json` via `JsonUtility`
- Color and head type sync to all clients on spawn via owner-writable `NetworkVariable`s

### Round System
- Sequential card draft: one eligible player picks at a time; others wait off-screen
- First draft: all players pick; subsequent drafts: only losers pick
- Scores tracked across rounds; first to 5 wins the match
- Map rotation cycles through Map1 → Map2 → Map3 → Map1... (expandable)
- Round-start countdown with "3 2 1 FIGHT!" overlay and audio; input locked until countdown completes

### Combat
- Server-authoritative ragdoll with client-predicted input
- Guns drop from sky on round start; can be thrown as a projectile (G key)
- Unarmed punch: lunge + AoE hit detection, knockback impulse
- Ricochet, ExplosiveRounds, and RapidFire cards modify bullet behavior per-round

---

## Project Structure

```
Assets/
├── Scenes/
│   ├── MainMenu.unity
│   ├── Lobby.unity
│   ├── CardDraft.unity
│   ├── Map1.unity
│   ├── Map2.unity
│   ├── Map3.unity
│   └── Results.unity
├── Scripts/
│   ├── Network/          RelayManager, NetworkInitializer, NetworkLobbyManager
│   ├── Player Logic/     PlayerController, PlayerCombat, PlayerHealth,
│   │                     ArmAimController, BalanceController,
│   │                     IgnoreLimbCollisions, RagdollSync, BulletVisual
│   ├── Weapons/          Gun, GunSpawner
│   ├── Cards/            CardDraftingUI, CardDatabase, CardIconRegistry,
│   │                     CardSlot, CardData, CardHUD
│   ├── Map Logic/        MapManager, ProjectilePool, RoundStartUI, ItemSpawner
│   ├── Items/            HealthPack
│   ├── Save/             SaveLoadManager, PlayerSaveData, SettingsSaveManager
│   └── UI/               UIManager, RoundProgressUI, ResultsManager,
│                         NetworkLobbyManager
├── Prefabs/
└── Audio/
```

---

## Technologies Used

- **Unity 2022.3.62f3**
- **C#** — game logic
- **Netcode for GameObjects 1.12.2** — server-authoritative multiplayer
- **Unity Relay 1.2.0** — online connectivity (join codes)
- **Unity Transport 1.5.0** — underlying network transport
- **TextMeshPro** — all UI text

---

## Music Credits

"Mesmerizing Galaxy" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/

"Equatorial Complex" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/

"Brain Dance" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/

All other music and sound effects sourced from Pixabay (pixabay.com).
