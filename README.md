# AbsoluteChaos - Multiplayer Arena Game

A fast-paced multiplayer arena game built in Unity where players compete to eliminate each other across rounds. The game features local multiplayer connectivity, a round-based scoring system, and core gameplay systems including player movement and match flow.

[GitHub Repository](https://github.com/BlueRobotMiner/AbsoluteChaos)


>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c
## Setup

### Prerequisites
- Unity 2022.3.62f3
- Git

### Installation

1. Clone this repository:
```bash
git clone https://github.com/BlueRobotMiner/AbsoluteChaos.git
```

2. Open the project in Unity Hub  
3. Open the MainMenu scene in Assets/Scenes/  
4. Press Play to test in the editor  

## How to Play

### Controls
- WASD: Move and Jump  

=======
- Mouse: Aim  
- Left Click: Shoot (Currently broken for Player 2)  
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c
- Escape: Pause menu  

### Objective
Eliminate all other players to win the round. Every round win earns 1 point. The first player to reach 5 points wins the match.

## Testing Multiplayer

=======

### Option 1: Local Network (LAN)
1. Build the project (File -> Build Settings -> Build)  
2. Run the built executable  
3. Press Play in the Unity Editor  
4. In one instance, click "Host Game"  
5. In the other instance, click "Join Game" and enter 127.0.0.1  
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c

### Option 2: Online (Unity Relay)
1. Host an "Online Relay" session from the Main Menu  
2. Copy the generated Join Code  

3. In the other instance, enter the code and click "Join"  
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c

## Project Structure

```
AbsoluteChaos/
├── README.md
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity
│   │   ├── Lobby.unity
│   │   └── Map1.unity
=======
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c
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

**Object Pool Pattern (Additional Design Pattern)**
- Location: Assets/Scripts/Combat/ProjectilePool.cs  
- Description: Pools projectile instances to improve performance during gameplay  

**Multiplayer Networking (Core System)**
- Location: Assets/Scripts/Networking/NetworkInitializer.cs  
- Description: Handles host/client setup and switches between Local IP and Unity Relay connections  


## Known Issues

- Combat system is partially implemented; Player 2 cannot shoot  
- Player card UI only displays one card instead of the full list  
- Movement physics feel floaty during jumps  
- Rematch system duplicates Player 1  
- Player 2 UI does not update correctly on some maps  
- Relay networking is not fully tested  
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c

## Future Enhancements (Final Submission)

- Implement working combat system for all players  
- Add SQLite database for persistent player statistics  
- Improve physics for more realistic movement  
- Complete Relay multiplayer system  
- Add AudioManager for music transitions  
- Replace placeholder graphics with finalized assets and visual effects  

## Technologies Used

=======
- Unity 6: Game engine  
>>>>>>> 861204d57d99d420027bbe83621b93f321d8e86c
- Netcode for GameObjects: Multiplayer networking  
- Unity Relay: Online connectivity  
- SQLite (planned): Persistent data storage  
- TextMeshPro: UI text rendering  
