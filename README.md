# Deadline

**A co-op multiplayer survival game: feed the shark before it eats your island.**

Built in **Unity 6** (URP) by [class1c](https://github.com/Class1c22). Players are stranded on a small procedurally generated island while a shark circles it and keeps biting pieces off it. The only way to stop it is to fish, find out what it likes to eat, and keep it fed.

<!-- Add screenshots or a GIF to a Docs/ folder and link them here, e.g. ![Gameplay](Docs/gameplay.gif) -->

**[Download the latest build (Windows)](../../releases/latest)**

---

## How the game works

- The island is a **procedurally generated mesh** and it is **physically eaten** as the game goes on: every shark bite deforms the terrain, and the palms on it disappear with it.
- Find the fishing rod somewhere on the island, cast it, wait for a bite, and strike at the right moment to catch a fish.
- Throw the fish into the sea and the shark swims over to eat it.
- The shark has **tastes that are randomized once per session**: a few fish species it likes (progress goes up) and the rest it hates (progress goes down). Players have to work out which is which.
- **Win:** the shark is fully fed (progress bar full).
- **Lose:** the island is completely devoured, or a player dies. Players can drown, because the oxygen bar drains while the camera is underwater.

Single player works too: if the game can't reach the Photon servers, it falls back to Offline Mode.

## Controls

| Input | Action |
|---|---|
| `W A S D` | Move |
| `Shift` | Sprint |
| `Space` | Jump |
| Mouse | Look around |
| `E` | Pick up nearby item |
| `R` | Throw the held item |
| Mouse wheel | Switch inventory slot (4 slots) |
| Right mouse button | Cast the line / cancel / strike when a fish bites |
| `Esc` | Release the cursor (menus, settings) |

## Technical highlights

**Networking (Photon PUN 2)**
- Master-client authority for shared world state: the shark and the island are scene objects, and only the master decides *when and where* the shark bites.
- Random values are rolled once on the master and the **finished result** is broadcast by RPC, so every client deforms the island identically. Everything else stays deterministic.
- Per-player systems (movement, camera, breathing, inventory UI) are switched off on remote avatars, so one client's input never drives another player's character.
- Restart goes through `RaiseEvent` instead of scene-bound RPCs, which is more robust for objects without stable view IDs.
- The connection flow retries with different protocols and finally falls back to Offline Mode.
- **Voice chat** with Photon Voice. A visualizer swaps the character's texture depending on how loud the player is speaking.

**Procedural world**
- `HeightmapIsland` builds the island mesh at runtime (smoothstep falloff + Perlin noise) and handles runtime deformation when the shark bites.
- Bite size is calculated automatically, so the island is guaranteed to be gone after exactly `totalBites` bites regardless of world size.
- `PalmSpawner` and `FishingRodSpawner` place objects by raycasting the terrain and rejecting spots by height, slope, distance from the shore and distance from other objects.

**Fishing system**
- A state machine covers cast, wait for bite, strike window, reel in, cancel and escape.
- The hook flies on a real parabolic trajectory.
- The fishing line is a **Verlet rope simulation** drawn with a `LineRenderer`.

**Shaders and VFX**
- Water built in **Shader Graph** over several iterations, plus an island blend shader.
- **GPU ripple simulation** on the water: a ping-pong chain of float render textures driven by custom shaders, fed by a top-down camera that sees objects touching the water.
- Particle VFX for water splashes and for the shark's reactions (hearts for a liked fish, angry marks for a disliked one).
- URP post-processing: vignette and film grain react to the player running out of oxygen.

**Player and animation**
- First-person controller with reduced gravity underwater.
- **Animation Rigging** (multi-aim constraint) for the head target, and a separate hand animator for the items the player holds.
- Oxygen system that compares the camera height against the water surface rather than just the body trigger.

**UI and audio**
- Menu, loading screen, settings, HUD, win and game-over screens, island health bar and fish progress bar.
- Inventory with animated slots, a catch-screen UI, and SFX for the shark, fishing, UI and tension.
- Custom splash screen with branding.

## Tech stack

| | |
|---|---|
| Engine | Unity 6 (6000.3.22f1) |
| Language | C# |
| Render pipeline | Universal Render Pipeline 17.3 |
| Shaders | Shader Graph + hand-written shaders |
| Networking | Photon PUN 2 |
| Voice | Photon Voice |
| Animation | Animation Rigging 1.4.1 |
| UI | uGUI + TextMesh Pro |
| Platform | Windows 64-bit |

## Project structure

```
Assets/
  scripts/
    Network/        Connection, spawning, restart flow
    island/         Island mesh, shark, bites, palms, fish zone
    Items/          Fishing rod, hook, rope line, spawner
    mainheroscripts/ Player controller, camera, breathing, death, pickup, voice
    inventory/      Inventory slots and equip events
    ui/             Menu, loading, HUD, win and game-over
  Models/Island/    Water and island shaders, ripple simulation
  Resources/        Networked prefabs (player, fish, island, shark bites)
  sfx/  vfx/  ui/   Audio, particle effects and UI art
  Menu.unity        Main menu scene
  GamePlay.unity    Gameplay scene
```

## Play the game

1. Download the latest zip from [Releases](../../releases/latest).
2. Extract it completely into a folder.
3. Run `Deadline.exe`.


## Run from source

1. Install **Unity 6000.3.22f1** with Unity Hub.
2. Clone the repository:
   ```
   git clone https://github.com/Class1c22/DeadLine.git
   ```
3. Open the folder in Unity Hub and let the project import.
4. Open `Assets/Menu.unity` and press Play. Multiplayer uses Photon Cloud and needs a Photon App ID in the Photon Server Settings. Without a connection the game runs in Offline Mode.

## Development workflow

Developed in feature branches and merged through **30+ pull requests**, with 100+ commits in small iterations. Typical areas covered by the history: water shaders and ripple simulation, the fishing flow, shark behavior and audio, HUD and menu layout, multiplayer fixes (room exit, restart flow), and URP performance tuning.

## Roadmap

- Optimize coop gameplay/ bug fix

## Credits

- [Photon PUN 2](https://www.photonengine.com/pun) and [Photon Voice](https://www.photonengine.com/voice) for networking and voice chat
- Unity packages: URP, Shader Graph, Animation Rigging, TextMesh Pro

## Author

**class1c**, game developer (Unity, Unreal Engine / UEFN).

GitHub: [@Class1c22](https://github.com/Class1c22)
