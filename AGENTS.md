# Vibe Coded Lacrosse — Agent Instructions

Unity 3D lacrosse game. Engine: **Unity 6**, Language: **C#**.

## Project Layout

```
Assets/Scripts/
  Ball/         BallController.cs         — physics, carry, cradle, launch
  Player/       PlayerController.cs       — third-person movement & input
                StickController.cs        — ball pickup, pass, shoot
  Managers/     GameManager.cs            — singleton state machine & timers
                ScoreManager.cs           — singleton score tracking
                GoalTrigger.cs            — OnTriggerEnter → ScoreManager
  UI/           HUDController.cs          — TextMeshPro HUD, event subscriber
  AI/           (empty — not started)
```

## Architecture Conventions

- **Singletons**: `GameManager` and `ScoreManager` use `Instance` pattern. Access via `GameManager.Instance` / `ScoreManager.Instance`.
- **Events (C# delegates)**: Cross-system communication uses public events — do **not** use `FindObjectOfType` for reactive updates.
  - `GameManager.OnStateChanged` — fires on state transitions (`WaitingToStart`, `Playing`, `Paused`, `GameOver`)
  - `ScoreManager.OnScoreChanged(Team team, int newScore)` — fires after every goal
- **Notification system** (planned): New UI notifications (goal banners, shot-clock warnings, game-over splash) should subscribe to existing events rather than being called directly. Add a `NotificationManager` singleton in `Assets/Scripts/Managers/` and a matching UI prefab in `Assets/Prefabs/`.
- **Ball physics**: Uses Unity 6 `Rigidbody.linearVelocity` and `linearDamping` APIs — do **not** use the deprecated `velocity` / `drag` properties.
- **Ball carry**: Call `ball.AttachToCarrier(stickSocket, carrierRoot)` to pick up; `ball.Release(launchVelocity)` to throw/shoot; `ball.Scoop(stickSocket, carrierRoot)` (two arguments) to auto-scoop a loose ball.
- **Audio**: `BallController` calls `AudioManager.Instance?.PlaySFX(clipName)` — an `AudioManager` singleton in `Assets/Scripts/Managers/` still needs to be created.
- **Teams**: The `Team` enum (`Home` / `Away`) is defined in `ScoreManager.cs`.

## Known Bugs / Missing Pieces

| # | File | Issue |
|---|------|-------|
| 1 | `StickController.cs` | Calls `ball.Scoop(stickHeadSocket)` — missing second argument `carrierRoot` (this `Transform`) |
| 2 | `Managers/` | `AudioManager` referenced but not yet created |
| 3 | `AI/` | Folder empty — no opponent/teammate AI |
| 4 | `Scenes/` | No scene assets wired up yet |
| 5 | `Prefabs/` | No prefabs created yet |

Fix bug #1 before adding features that depend on scooping.

## Notification System Focus

When building notifications, follow this pattern:
1. Subscribe to `GameManager.OnStateChanged` and `ScoreManager.OnScoreChanged` in `NotificationManager.Start()` — unsubscribe in `OnDestroy()`.
2. Use a pooled `NotificationUI` prefab (canvas-space) driven by a queue so overlapping events don't stomp each other.
3. Expose methods like `ShowGoalBanner(Team team)`, `ShowShotClockWarning()`, `ShowGameOver()` — keep display logic in the prefab's own script.
4. Shot-clock countdown warning should trigger when `GameManager.ShotClockRemaining < 10f` — poll in `Update` or add a dedicated event to `GameManager`.

## Build & Play

Open the project in Unity 6, open the main scene from `Assets/Scenes/`, press **Play**. There are no automated tests yet.
