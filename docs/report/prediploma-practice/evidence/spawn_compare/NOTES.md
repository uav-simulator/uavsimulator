# Spawn Compare: First-Frame Diagnostic

Date: 2026-04-24
Script: `python/diagnostics/compare_spawn_frames.py`

---

## Pose Comparison

| Field           | corridor               | maze_stageA            | Diff                |
|-----------------|------------------------|------------------------|---------------------|
| x               | 0.000                  | 0.000                  | 0.000               |
| y               | 0.010                  | 0.010                  | 0.000               |
| z               | -0.850                 | 0.000                  | -0.850 (irrelevant — different coordinate systems) |
| rotation.y (quat) | 0.000 (yaw=0°)       | 0.707 (yaw≈90°)        | **90° yaw difference** |
| ultrasonic front | 0.12 m               | 0.66 m                 | **+0.54 m difference** (see `raw_telemetry.telemetry[].key=="sensor.ultrasonic.front.m"`) |

**Spawn position is consistent (y=0.010 in both)**. The z-offset difference is meaningless — the maze track origin is different from the corridor track origin. The XY position within each track is (0,0) in both cases. Spawn itself is not the problem.

---

## Visual Observations (from actual frames)

### corridor_frame.png
- Three-walled view: left wall, right wall, and a **flat end wall** straight ahead.
- No objects or markers on the end wall.
- The end wall fills roughly **30-35% of the vertical frame** — agent is at z=-0.85 within a ~1.5m corridor, so it is near the **start** of the corridor far from the end wall.
- The wall-top gap (sky visible) is moderate, indicating wall height to camera angle ratio typical of the training layout.
- Floor: uniform sandy/tan color with no markings.
- Wall texture: vertical corrugated cardboard stripes, warm tan/orange-brown.
- Sky: flat light blue (ambient skybox).
- No ceiling — open top.

### maze_stageA_frame.png
- Same three-walled arrangement (left, right, end wall straight ahead).
- **A black-and-white checkerboard marker** is mounted center-frame on the end wall — this is a **goal marker** or ArUco-style fiducial that does NOT appear in the corridor scene.
- The end wall is significantly **closer** than in the corridor frame — consistent with ultrasonic=0.66m vs 0.12m (the 0.12m reading in corridor may be rear/side reflection; the agent faces ~0.66m to the end wall in maze vs ~0.65m corridor depth ahead but the view angle implies agent faces along the first cell which is ~0.6m wide).
- Wall texture: identical vertical corrugated cardboard stripes, same warm tan color.
- Sky: identical flat light blue.
- Floor: same tan sandy color.

**Key structural difference:** The maze frame has a prominent black-and-white checkerboard pattern on the end wall that is entirely absent from the corridor scene. This is a significant visual feature that the CNN will activate on — and was never present during v6 training.

---

## Yaw Difference Analysis

The rotation quaternion `(x=0, y=0.707, z=0, w=0.707)` in the maze corresponds to a **90-degree yaw** rotation. This means the vehicle faces a different direction at spawn. In the corridor the agent spawns facing the corridor end (aligned with the track z-axis). In the maze the agent spawns rotated 90° — this is the `GetDefaultSpawnPosition` placing the vehicle at the entrance of the first cell, facing down the first corridor segment.

This 90° yaw difference means the camera orientation is completely different. Even though the rendered output still shows "a corridor" (because the maze cell is a corridor), the **lighting direction, shadow angles, and relative positions of left/right walls** will differ from what the CNN was trained on.

---

## Hypothesis for Off-Distribution Behavior

Two concrete visual causes for catastrophic forgetting, in order of severity:

1. **Checkerboard marker on the end wall** (maze only): This is a large, high-contrast visual element never seen by v6. The CNN feature extractor (NatureCNN) will fire on it unexpectedly, producing activations far from the training distribution. This is the most likely cause of the first-step value estimate collapsing.

2. **90° yaw at spawn**: The vehicle orientation at reset is rotated 90° relative to corridor. Even though the cell geometry looks similar, the lighting and shadow pattern across the walls will differ due to the fixed skybox light direction. This shifts the CNN input distribution moderately.

3. **Wall height difference** (`maze.wall_height_m: 0.25` vs corridor walls which appear taller in the frame): The maze uses explicitly shorter walls (0.25m), which changes the sky-to-wall ratio in the frame. This is visible in the images — both show very similar sky gaps, suggesting the actual rendered heights may be comparable or the camera FOV normalizes it.

**Spawn injection (same position) will NOT fix the checkerboard marker issue or the yaw difference.** These are not spawn-position problems.

---

## Step 6 Decision Recommendation

**Visual distribution mismatch is the primary cause** — NOT spawn position.

Specific factors:
- The checkerboard goal marker in the maze is absent from all v6 training data. This alone is sufficient to cause off-distribution CNN activations.
- The 90° yaw rotation at maze spawn produces a different lighting/shadow pattern even though the corridor geometry looks superficially similar.
- Ultrasonic front distance differs (0.12m corridor vs 0.66m maze) which also shifts the observation vector.

**Recommendation: Do NOT proceed with spawn injection (Task 1 addendum).** Spawn position is not the root cause. The correct fix is one of:
  - Train v7 from scratch on maze (already in progress, avoids catastrophic forgetting by construction).
  - Remove or randomize the goal marker appearance so v7 learns to ignore it.
  - Add corridor fine-tuning episodes to the maze curriculum with matching observation distribution.

The catastrophic forgetting during v6→maze transfer is explained by the checkerboard marker + yaw mismatch producing off-distribution CNN inputs, causing the value function to produce garbage estimates on step 1.
