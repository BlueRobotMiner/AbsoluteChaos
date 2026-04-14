# AbsoluteChaos - Multiplayer Arena Game

A fast-paced multiplayer arena game built in Unity where players compete to eliminate each other across rounds. The game features local multiplayer connectivity, a round-based scoring system, and core gameplay systems including player movement and match flow.

[GitHub Repository](https://github.com/BlueRobotMiner/AbsoluteChaos)

## Known Issues

- I am prioritizing fixing local first.
- (Fixed)Combat system is partially implemented; Player 2 cannot shoot   
- Player card UI only displays one card instead of the full list  
- (fixed Due to the player not being able to move left and right while they jump)Movement physics feel floaty during jumps  
- Rematch System only displays player one now doesn't respawn player 2 and the rematch system puts them in the lobby instead of it the draft first draft again 
- (Fixed Kinda) Player 2 UI does not update correctly on some maps  
- Relay networking is not fully tested Some issues are bound to be here
- Player cannot throw gone when it's above their head for some reason Also I believe layer two can't throw their gun in general.
- When the game is over guns do not despawn when utilizing the rematch button.
- Player cards are not fully implemented does not currently exactly do anything.
- Guns still currently fall through the map But they respawn themselves back at the spawn point if they go too far off the screen.

## Additional bug fixes
- Players not being able to jump move around correctly and map has been fixed due to me forgetting to put a box Collider on the ground latforms

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
- Space: Jump
- Mouse: Aim  
- Left Click: Shoot/ Punch if you don't have a gun (No animation currently) 
- G: Throw gun
- Escape: Pause menu  

### Objective
Eliminate all other players to win the round. Every round win earns 1 point. The first player to reach 5 points wins the match.

## Testing Multiplayer
Currently only tested with two people even though for the final roject it is supposed to be working with three people Reason why testing like this is it's easier to find bug fixes to make sure that player three can also work And so on

### Option 1: Local Network (LAN)
1. One on player one screen click and then click Create local Then it will put you in a lobby to show you your I
2. When that lobby is created player two screen if you're using the same C to test this your join hitting join and then join local button the text box above should already implement the local IP
3.

### Option 2: Online (Unity Relay)
1. Host an "Online Relay" session from the Main Menu  
2. Copy the generated Join Code  
3. In the other instance, enter the code and click "Join"

Here's how a generation you click host Create public it gives a code in the top left of the lobby screen you give that code to the second lay they enter that code when they hit join above join public and then it should connect them But if you're using this as in Unity Unreal you might not get it working. I believe for it work it would have to be a full build.

## Project Structure

```
AbsoluteChaos/
├── README.md
├── Assets/
│   ├── Scenes/
│   │   ├── MainMenu.unity
│   │   ├── Lobby.unity
│   │   └── Map1.unity
        └── Results
│   ├── Audio/
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


## Future Enhancements (Final Submission)

- Implement working combat system for all players  
- Add SQLite database for persistent player statistics  
- Improve physics for more realistic movement  
- Complete Relay multiplayer system  
- Add AudioManager for music transitions  
- Replace placeholder graphics with finalized assets and visual effects  

## Technologies Used

- Unity 2022.3.62f3  
- C#: Game engine  
- Netcode for GameObjects: Multiplayer networking  
- Unity Relay: Online connectivity  
- SQLite (planned): Persistent data storage  
- TextMeshPro: UI text rendering  
