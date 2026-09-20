# Quick controller check

Open **Fruit Fly → Simulation Controls** in Unity.

For immediate controller response choose **FlyWire + FlyBody**, disable **Detailed MuJoCo body**, select **Combat prototype**, and click **Apply mode and run**. Click Game view. The HUD should show **Xbox-compatible gamepad**.

| Input | Expected result |
| --- | --- |
| Left stick back | Climb |
| Left stick forward | Dive |
| A tap | Initial speed burst settling to a smaller cruising boost |
| B tap | Brake |
| B hold | Approach and land/perch |
| Right stick | Independent look/aim |
| X | Deliberate dismount near a safe perch, or remount nearby |
| Y | Switch weapon |
| RT | Sword on foot; couch lance while mounted |
| LT hold/release | Draw/release bow |
| Start | Reset |

For scientific-body combat choose **Eon / NeuroMechFly**, **Floor**, **Walking / perch / assisted launch**, and **Apply walking mode and run**. The full-brain toggle is optional. Add combat practice in Game view; toggle Enemy combat for swordsmen/archers. Holding B stops gait and establishes a verified claw hold before X can dismount. A launches with engineering assistance; stick input resumes gait while perched. The body runs slowly and combat uses its simulation clock. This mode does not yet implement validated wing-driven climb/dive.

**Thorax leg grip** changes artistic mounted leg placement. Dismount physically unloads the fly; remount restores the one-third rider load. P pauses the scientific body, arrows and rider animation together.

**Borrowed mount transitions (experimental)** optionally plays the Silverspur mounting/dismounting clips and moves the visual rider between anchors. It defaults off. These horse-based movements need fitting to the fly; the actual gameplay attachment and mass handover remain immediate.

Tell me which inputs feel wrong, whether aiming is comfortable, and whether the rider now sits where you intended. Automated checks cannot establish the feel of your physical controller. Current execution evidence and research limits are in Research/CURRENT_STATUS.md.

## Detailed appearance, roll and dismounted life
Fast mode now defaults to the 69-part NeuroMechFly mesh used in scientific walking tests, with an appearance toggle. LB/RB (keyboard Z/V) rolls around the forward axis; held B prioritizes the surface toward the feet. Legs replay recorded MuJoCo gait; head scanning and wing animation are artistic. Scientific shoulder-roll flight remains unfinished.

Optional dismounted walking uses bounded geometric surface walks in fast mode, and actual unloaded MuJoCo leg gait in floor WalkingLab. Scientific bouts return to verified claw holds for remounting. Turn the walking toggle off to remain perched. Wall/ceiling autonomous bouts are disabled. Scheduling is engineering behavior, not biological decision-making.

New verification: Unity fast-mode checks passed all 19 cases. Live body-only idle evaluation passed with 2.1745 mm maximum excursion, 0.6071 rad joint change, six final claw contacts, and rider load restored without resetting time. Evidence: Research/combat-play-evaluation.json and Research/walking-idle-evaluation.json. The earlier 14-case fast result above is superseded.
Floor WalkingLab: enable Trained head stabilization (floor) in Simulation Controls and Apply to use the upstream trained proprioceptive controller on actual neck joints. Head status appears in the HUD. It defaults off; wall/ceiling use the prior passive head. This is not FlyWire head-neuron control.
