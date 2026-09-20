# Controlled same-body floor transition

The trained FlyBody walking and flight policies now complete a controlled floor launch, wing-powered hover, landing, two-second claw hold and six seconds of sustained native walking on the same persistent physical body. The rider remains one third of the original fly mass. Qualification limits were preserved.

Five initializations passed in `unified-contact-qualification-20260918T213219801072Z-summary.json`. Each achieved minimum walking up .9661534722, forward travel 5.866619819 cm and final-quarter forward travel .244126501 cm. Measured wing command-phase alignment deliberately makes these controlled launch trajectories identical; this is repeatability evidence, not arbitrary airborne-state robustness.

The successful handoff uses measured loaded stance commands, physical completion of unsupported foot contacts, a consistent synthetic heading-reference quaternion sign, and native motor/claw scheduling without an additional command or grip blend. Physical activation filters remain active. No root force, body-state reset or time reset is introduced. Failed experiments remain saved.

## Unity use

Open `Assets/UnifiedFloorLab.unity`, then enter Play. The qualified backend starts automatically; startup rejects a missing qualification or changed controller source hashes. P pauses/resumes, Backspace explicitly replays, right stick or mouse looks, and R recenters. The completed run waits for explicit replay. Exiting Play stops the backend owned by the editor.

For verification, enter Play and promptly choose **Fruit Fly → Research → Run Unified Floor Viewer Checks**. Results are saved as unique `unified-viewer-*-evaluation.json` files, with side/front captures from flight, held landing and walking.

Final Unity verification passed all nine checks in `unified-viewer-20260918T215219499-evaluation.json`: 85 geometries, zero rendered position/rotation error, exact pause, resumed whole qualification and explicit new-session replay. All three phase captures were reviewed. Gameplay regression passed 30 checks; rider pose regression passed three orientations and nine transitions. Tests and owned backend are stopped.

The separate transport regression `unified-stream-20260918T214121529632Z-evaluation.json` passed 754 actual physical frames, exact paused clock/geometry, resumed full qualification and a new session on explicit replay. Copied handoff forecasts are excluded from the live stream.

## Scope and provenance

This lab is an automated engineering trajectory using trained body policies. Live reins and connectome motor control are not connected here. It does not establish biological validation, arbitrary landing recovery or wing-powered wall/ceiling transitions. The existing playable brain steering and NeuroMechFly modes retain their separate provenance and qualification. Human seating and foot IK are visual approximations; the rigid rider load is the physical approximation.
