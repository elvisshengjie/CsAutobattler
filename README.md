# CS Autobattler

A work-in-progress, CS:GO-inspired **2.5D top-down shooter autobattler** built with Unity and C#. It combines tactical squad combat with a shop inspired by **Teamfight Tactics** and **Dota Auto Chess**, plus a roguelike run structure: **lose a level and your run is over**.

Recruit a squad, manage your gold, choose your deployment and tactics, and let your agents move and fight automatically. During combat, you can influence the battle through team commands and character abilities.

## Project status

**This project is in active development.** The current implementation is a three-round campaign prototype. Gameplay, balance, visuals, UI, and content are still evolving.

The campaign currently reloads the map between rounds while retaining your roster and run upgrades. A defeat clears your roster, upgrades, shop state, and gold progress, then returns you to the main menu for a fresh run.

## Gameplay loop

1. Select **Play** and choose one free C-tier starter from three different role offers. You begin with 5 gold.
2. Preview the next enemy squad, its tactic, and any stat buffs.
3. Buy characters and squad modifiers, reroll the shop, or freeze its offers for the next round.
4. Deploy up to five characters. Extra recruits go to the bench; the total roster holds up to eight characters.
5. Lock your deployment and choose an opening team tactic. Recruits keep the role and weapon they were purchased with.
6. Fight an automated tactical battle, using commands and abilities when needed.
7. Win to earn gold and advance. Clear all three rounds to complete the prototype campaign; lose any round and start a new run.

## Current features

### Recruitment and economy

- Three character offers and a separate modifier offer in the shop.
- C-, B-, and A-tier characters, with tiers affecting combat stats and role abilities.
- Character prices of **3 / 5 / 8 gold** for C / B / A tiers.
- Shop rerolls cost **1 gold**; modifiers cost **3 gold**.
- Victory income of **5 gold**, plus **1 interest per 5 saved gold**, capped at 5 interest.
- Persistent recruitment and upgrades within a run, with units restored to full health between rounds.

Available modifiers include stackable health and weapon damage upgrades for the run, plus temporary opening effects that speed up allies or slow enemies for the first 10 seconds of the next battle.

### Squad combat and tactics

- Four character roles: **Support, Flanker, Assaulter, and Defender**.
- Four weapon types: **Rifle, SMG, Sniper, and Shotgun**.
- Opening tactics: **Fast Execute, Feint and Rotate, Split Push, and Silent Infiltration**.
- Mid-round commands: **Regroup, Plant, Defend, and Retreat**.
- Bomb-site objectives, planting, defusing, elimination checks, and round timers.
- Role abilities, including support healing and an assaulter turret, with a player ability targeting interface.
- Tactical slow motion powered by a draining and recovering focus resource.
- AI systems for sensing, target selection, role positioning, coordinated movement, and defender rotations.
- A* pathfinding, position reservations, and influence maps for navigation and tactical positioning.
- Team status panels, health bars, a minimap, objective timers, and round results.

Campaign enemies are currently always visible. Team-vision visibility rules also exist for play outside the campaign.

### Prototype encounters

| Round | Encounter | Opposition |
| --- | --- | --- |
| 1 | Opening Skirmish | One C-tier rifle defender using a basic hold tactic. |
| 2 | Split Response | Two C-tier enemies using split defense, with +5% health and damage. |
| 3 | Coordinated Retake | Three mixed B/C-tier enemies using aggressive rotations, with +12% health and +8% damage. |

These encounters are defined in `CampaignRoundDefinition.CreatePrototypeRounds()` in [CampaignTypes.cs](Assets/Scripts/Campaign/CampaignTypes.cs). They currently form a fixed sequence rather than a procedurally generated campaign.

## Getting started

### Requirements

- **Unity 6000.5.1f1**, the editor version recorded in `ProjectSettings/ProjectVersion.txt`.
- Unity Hub to install and open the matching editor.
- A C# editor such as Visual Studio or Rider for code changes.

The project uses the Universal Render Pipeline, Unity Input System, and Unity UI. Package dependencies are recorded in [Packages/manifest.json](Packages/manifest.json).

### Run in the editor

1. Clone or download this repository.
2. In Unity Hub, add the project folder containing `Assets`, `Packages`, and `ProjectSettings`.
3. Open it with Unity **6000.5.1f1** and allow asset import and package resolution to finish.
4. Open `Assets/Scenes/Map1.unity`, the first enabled build scene.
5. Enter Play Mode and select **Play** in the game's main menu.

The campaign system initializes automatically for scenes containing a `RoundManager`. Other scenes in the repository include `Map2`, `BattleScene`, and `SampleScene`; use `Map1` as the starting point for the current campaign.

## Controls

| Input | Action |
| --- | --- |
| Right mouse button + drag | Pan the top-down camera. |
| Mouse wheel | Zoom toward the cursor. |
| Hold Space | Activate tactical slow motion while focus is available. |
| On-screen shop and deployment controls | Recruit characters, buy modifiers, and manage the squad. |
| On-screen tactic and ability controls | Issue squad commands and select abilities. |
| Left click while targeting an ability | Confirm a valid target or placement. |
| Right click or Escape while targeting | Cancel ability targeting. |

## Source layout

| Path | Purpose |
| --- | --- |
| `Assets/Scenes/` | Maps and gameplay scenes. |
| `Assets/Scripts/Campaign/` | Run progression, recruitment, shop, economy, and encounter definitions. |
| `Assets/Scripts/Agents3D/` | Agent brains, sensors, memory, movement, and position reservations. |
| `Assets/Scripts/AI/` | Roles, abilities, squad tactics, defender coordination, and influence maps. |
| `Assets/Scripts/Combat/` | Weapons, projectiles, health, stats, and visibility. |
| `Assets/Scripts/Objectives/` | Bomb objectives and round state management. |
| `Assets/Scripts/Grid3D/` | 3D pathfinding and influence-map visualization. |
| `Assets/Scripts/Camera/` | Camera controls. |
| `Assets/Scripts/UI/` | HUD, minimap, team panels, ability commands, and tactical slow motion. |
| `Assets/Editor/` and `Assets/Scripts/Editor/` | Scene builders, migration helpers, and editor tooling. |
| `Assets/Tests/` | Regression tests for campaign, movement, roles, visibility, round results, and slow motion. |
| `Packages/` and `ProjectSettings/` | Unity dependencies and project configuration. |

## Development and validation

Use Unity's Test Runner to run the Edit Mode regression tests under `Assets/Tests/`. For gameplay changes, also validate matches in Play Mode, especially navigation through doorways, squad coordination, planting and defusing, and the campaign's victory and defeat flow.

Additional development notes:

- [AI stability and squad-size behavior](documentation/AI_STABILITY.md)
- [Original campaign prototype notes](CAMPAIGN_PROTOTYPE.md) — historical context; the retry behavior described there has been replaced by run-ending defeat in the current source.

The current focus is developing and refining the prototype. Feature descriptions and balance values in this README reflect the present source code and may change as the project grows.
