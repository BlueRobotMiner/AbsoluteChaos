# AbsoluteChaos - Multiplayer Arena Game

A fast-paced multiplayer arena game built in Unity where players compete to eliminate each other across rounds. The game features local and online multiplayer support, round-based scoring, and experimental combat systems currently in development.

## Setup

### Prerequisites
- Unity 6
- Git

### Installation

1. Clone this repository:
```bash
git clone https://github.com/yourusername/absolutechaos.git
```

2. Open the project in Unity Hub  
3. Open the MainMenu scene in Assets/Scenes/  
4. Press Play to test in the editor  

## How to Play

### Controls
- WASD: Move and Jump  
- Mouse: Aim  
- Left Click: Shoot (Currently broken for Player 2)  
- Escape: Pause menu  

### Objective
Eliminate all other players to win the round. Every round win earns 1 point. The first player to reach 5 points wins the match.

## Testing Multiplayer

### Option 1: Local Network (LAN)
1. Build the project (File -> Build Settings -> Build)  
2. Run the built executable  
3. Press Play in the Unity Editor  
4. In one instance, click "Host Game"  
5. In the other instance, click "Join Game" and enter 127.0.0.1  

### Option 2: Online (Unity Relay)
1. Host an "Online Relay" session from the Main Menu  
2. Copy the generated Join Code  
3. In the other instance, enter the code and click "Join"  

## Project Structure

```
AbsoluteChaos/
├── README.md
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity
│   │   ├── Lobby.unity
│   │   └── Map1.unity
│   ├── Scripts/
│   │   ├── Managers/
│   │   ├── Player/
│   │   ├── Combat/
│   │   └── UI/
│   ├── Prefabs/
│   └── Audio/
├── Packages/
└── ProjectSettings/
```

## Technical Implementation

**Singleton Pattern**
- Location: Assets/Scripts/Managers/GameManager.cs  
- Description: Manages match scoring, win condition (first to 5), and scene transitions  

**Object Pool Pattern**
- Location: Assets/Scripts/Combat/ProjectilePool.cs  
- Description: Pools projectile instances for improved performance during combat  

**Hybrid Networking**
- Location: Assets/Scripts/Networking/NetworkInitializer.cs  
- Description: Switches between Unity Relay and Local IP transport depending on connection type  

## Known Issues

- Combat projectiles are currently disabled; Player 2 firing is broken  
- Player card stack only displays one card instead of the full list  
- Air movement feels floaty instead of using natural gravity  
- Rematch spawns a duplicate Player 1 instance  
- Player 2 UI does not update correctly on some maps  
- Lobby and Relay systems are not fully tested  

## Future Enhancements (Final Submission)

- Card icon system to replace text-based UI  
- SQLite integration for lifetime stats tracking  
- AudioManager for music transitions  
- Visual upgrade with neon assets and bloom effects  

## Technologies Used

- Unity 6: Game engine  
- Netcode for GameObjects: Multiplayer networking  
- Unity Relay: Online connectivity  
- SQLite (WIP): Persistent data storage  
- TextMeshPro: UI text rendering  
