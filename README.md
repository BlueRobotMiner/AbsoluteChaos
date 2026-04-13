# Absolute Chaos

**Developer:** Zachary Ziegler | **Studio:** Cyan Circuit Games
**Platform:** PC (Unity) | **Multiplayer:** 3-Player Networked

Absolute Chaos is a frantic 2D platformer PvP shooter built in Unity. The core hook is a physics-based brawler where players stack game-breaking ability cards, turning a simple shootout into absolute bullet-hell mayhem. The game features a "Cyber/Neon" aesthetic with stylized stick figures to prioritize technical logic and networking over asset creation.

## Setup

### Prerequisites
- Unity 6 (or Unity 2022.3 LTS)
- Git
- Unity Netcode for GameObjects package
- Unity Relay package

### Installation
1. Clone this repository:
   ```bash
   git clone [https://github.com/BlueRobotMiner/AbsoluteChaos.git](https://github.com/BlueRobotMiner/AbsoluteChaos.git)
Open the project in Unity Hub.

Ensure Unity Gaming Services is linked in Project Settings for Relay support.

Open the Scenes/MainMenu scene and press Play.

How to Play
Controls
WASD: Move and Jump

Mouse: Aim weapon

Left Click: Shoot (Work in Progress)

Escape: Pause Menu

Objective
Be the first player to reach 5 points. After each round, the losers draft "Chaos Cards" that modify their physics, weapons, or environment. The game rotates between three different maps throughout the match.

Testing Multiplayer
Option 1: Local Network (LAN) - Current Primary Focus
Development is currently prioritized on local stability to ensure the base code and player logic are solid before expanding to public lobbies. Local IP connection (127.0.0.1) is the recommended method for evaluating the current build.

Build the project (File -> Build Settings -> Build).

Run the built executable.

In the Unity Editor, click "Host" -> "Local Network".

In the standalone build, click "Join", enter the Host's local IP, and connect.

Option 2: Online (Unity Relay) - In Development
The Public Lobby and Relay implementation is a future project milestone aiming for completion by next Sunday.

Host an "Online Relay" session from the Main Menu.

Copy the generated Join Code displayed in the Lobby.

Clients enter the Join Code on their Main Menu to connect via Unity Relay.

Technical Implementation (Unit 13 Draft)
Singleton Pattern

Location: Assets/Scripts/Managers/GameManager.cs

Description: Manages match scoring, the "First to 5" win condition, and handles scene transitions.

Object Pool Pattern

Location: Assets/Scripts/Combat/ProjectilePool.cs

Description: Manages neon bullet instances to optimize network performance and reduce memory overhead.

Multiplayer Networking (Hybrid Transport)

Location: Assets/Scripts/Networking/NetworkInitializer.cs

Description: Implements a custom system to switch between Unity Relay for online play and Local IP Transport for offline testing.

Audio & Lighting (Neon Aesthetic)

Location: Assets/Scenes/Map1.unity

Description: Utilizes Global Illumination and Post-Processing (Bloom) to create a glowing neon atmosphere.

Known Issues (Unit 13 Draft)
Connectivity: The Lobby implementation has not been fully tested in a live environment yet as development is currently prioritized on local player stability.

Combat: Projectiles are currently disabled; the gun rotates toward the mouse, but the left-click firing logic is currently broken for Player 2 and is being refactored.

Card Selection UI: The player's current card stack incorrectly displays only one card at a time instead of a full history of chosen abilities.

Drafting UI: The stick figure character does not yet "run" toward the hovered card during the drafting phase.

UI Desync: Player 2's screen currently fails to update map-specific UI elements and game state markers.

Rematch Bug: Triggering a rematch incorrectly spawns a duplicate "Player 1" instance.

Physics: Character movement in the air feels "floaty" or "gliding" rather than following natural gravity when moving left or right.

Audio: The AudioManager does not yet correctly transition music tracks between the drafting phase and active maps.

Future Enhancements (Final Submission)
Card Icon System: Replacing text-based card lists with a visual HUD showing custom icons for every ability a player has drafted.

Steam Integration: Implementing Steamworks.NET for Steam Datagram Relay support (Smart Transport Switching).

SQLite Integration: Lifetime stat tracking (Total Wins, Games Played) and local settings persistence.

Drafting Logic Completion: Fully implementing the physics and weapon modifiers associated with the Chaos Cards.

VFX & Audio Polish: Adding localized sound effects, dynamic background tracks, and particle trails.

Technologies Used
Unity 6: Game Engine

Netcode for GameObjects: Multiplayer networking framework

Unity Relay: Online connectivity

TextMeshPro: UI text rendering

SQLite: Database for persistent storage (WIP)
