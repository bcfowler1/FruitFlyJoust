# Research backend evaluation

NeuroMechFly WalkingLab now includes optional full-connectome steering, same-body claw/assisted-launch transitions, physical rider unloading/remounting, gameplay combat on the simulation clock, and an exact observation-cache optimization. See CURRENT_STATUS.md for latest checks and limitations. This is our public-component integration, not Eon's complete embodiment or validated biological sensory/motor control. Unified wing flight remains experimental.

This directory records work toward the required embodied fruit-fly brain. It does not switch the playable Unity prototype to a neural backend.

Current status: FlightLab supports a loaded physical fly and an explicitly experimental full-connectome closed loop. ResearchLab supports separate six-claw wall/ceiling contact experiments. The rider mass is one third of fly mass. See FLIGHT_PROGRESS.md for current validation and remaining biological, combined-control, and real-time limitations. Earlier sections below record the original evaluation stage.

Sources downloaded to the workspace's work/research directory:

| Component | Source | Pinned revision |
| --- | --- | --- |
| FlyWire-derived whole-brain LIF code/data | https://github.com/eonsystemspbc/fly-brain | a3db62f9436074e485c0278290c2164ed6150808 |
| Articulated fly body and flight/walking tasks | https://github.com/TuragaLab/flybody | d015e9bfe441bd90ae431bac24c55cb74bdbce26 |
| NeuroMechFly / FlyGym (source inspection only) | https://github.com/NeLy-EPFL/flygym | 38c8ec61034cd59bc5ba0de20688d4a3c0000d60 |

The public brain repository includes FlyWire v783 neuron metadata and weighted connectivity. Its CLI is a benchmark runner, not a ready-made streaming body controller. Eon's own [embodiment explanation](https://eon.systems/updates/embodied-brain-emulation) describes linking selected descending neural outputs to body-level controllers. We still need to implement and validate a flight-specific mapping; neither rein signals nor brain activity can be assigned arbitrarily to wing actuators and described as validated biology.

Native Windows evaluation uses a workspace-local Python environment, Brian2's NumPy CPU target, and MuJoCo. The upstream [GPU setup](https://github.com/eonsystemspbc/fly-brain) is documented for Linux/WSL and CUDA; WSL is not installed on the current machine. The detected GPU is an RTX 5070 Ti with 16 GB memory. Its existence does not establish real-time whole-brain/body throughput.

Run evaluate_backend.py with `body` or `brain` using the workspace-local research-env Python executable. Results are saved here as body-evaluation.json and brain-evaluation.json. The body test loads the upstream articulated XML and advances 100 zero-actuation physics steps, optionally rendering a preview. This is not controlled locomotion. The brain test builds the released full network, stimulates upstream sugar-sensing neurons at the repository's 200 Hz setting, and advances 20 ms with a fixed seed. It reports actual runtime and propagation, not biological accuracy or flight control.

The original Brian2 reset contains a `w = 0` assignment even though w is a synaptic variable, not a neuron variable. The evaluation removes that undefined neuronal assignment in its parameter dictionary and records the adaptation; upstream source remains unchanged.

Next integration gate: identify flight-related input/output pathways and choose a tested body controller/wing model. Persistent GPU stepping and the synchronized pose transport have now been tested independently of Unity playback. Rider gameplay remains in Unity. Preserve authorship and license notices when distributing third-party code/data.

## Initial results

Both independent smoke tests passed. The body loaded with 68 bodies, 103 joints, and 78 actuators, then advanced 100 steps of 0.1 ms with finite state. The rendered preview is body-preview.png. This test used no learned locomotion controller.

The released brain data built a network of 138,639 neurons and 15,091,983 weighted directed connection rows. Twenty-one sugar sensory neurons were stimulated; the test recorded 158 spikes across 83 neurons, including 62 outside the stimulated set. Setup took 2.07 seconds; 20 ms of simulated activity took 1.65 seconds on the NumPy CPU target (about 83 times slower than real time). These are one short run's measurements, not a biological-accuracy score or steady-state performance guarantee.

## GPU and Unity bridge

The published flight controller now has a separate FlightLab scene and backend. See UNITY_FLIGHT.md for launch/controls and validation. It streams learned-controller motion with a thorax-mounted visual rider, pause/replay, and action clipping diagnostics. FlightLab has no neural coupling; ResearchLab retains the full GPU brain with passive body physics.

Native Windows CUDA execution passed on the RTX 5070 Ti with PyTorch 2.8.0+cu128. A deterministic small-network CPU/GPU comparison passed. Full-network stepping measured about 0.18 times real time; persistent CUDA graph stepping reached about 0.24 times real time (roughly four times slower than real time). Allocated GPU memory was about 198 MB. See gpu-evaluation.json for measurements. These checks establish execution, not biological validation.

The research viewer imports 85 visual geometries from the articulated FlyBody model, retaining its Apache-2.0 license notice. The UDP bridge is restricted to localhost. It advances brain and passive body on synchronized 0.1 ms simulation clocks, sending poses every 15 ms of simulated time. Positions are converted from centimeters to meters in Python, then displayed at 500 Unity units per meter. Unity swaps Y/Z and reverses mesh winding and quaternion imaginary components to convert coordinate handedness. The mounted rider follows the thorax; rider mass and forces are not included in MuJoCo.

In Unity, stop Play, then select **Fruit Fly → Research → Create Research Scene**. This creates Assets/ResearchLab.unity. Enter Play, then select **Fruit Fly → Research → Start GPU Backend**. Start after entering Play because an assembly reload stops the owned backend process. T toggles sugar sensory stimulation; P pauses both simulation clocks. Right stick or mouse controls the view. Stop GPU Backend ends the process; assembly reload and Editor shutdown also stop it.

The backend depends on this workspace's work/research source downloads and work/research-env Python environment. The research scene is separate from Practice.unity. It displays actual passive body poses and neural telemetry; it does not yet use neural outputs to control flight. On September 17, 2026, the scene was generated, imported, and visually tested in Unity 2022.3.62f3 Play mode. Live streaming, sensory stimulation, pause, camera recentering, and stream-disconnection status were observed. Import testing found and fixed the missing JSON serialization module. The rider now follows the thorax body frame rather than its visual mesh frame; research camera framing and paused speed reporting were corrected. The backend tolerates Windows UDP connection-reset notifications when the viewer is absent. Actual combined throughput in these Editor runs was substantially slower than the neural-only benchmark (roughly 0.04–0.15 times real time), so this remains a research viewer rather than a real-time neural flight game.

The current FlyGym 2.1.0 source requires NumPy 2.x and MuJoCo 3.9.x, whereas FlyBody pins NumPy 1.26.4 and the tested dm_control version pulls MuJoCo 3.13.0. Evaluate FlyGym in a separate environment rather than forcing both dependency sets into this one. The brain evaluation uses Brian2 2.8.0, PyArrow 25.0.1, and NumPy 1.26.4; the body evaluation uses MuJoCo 3.13.0.

## Current playable head system

Floor WalkingLab offers **Trained head stabilization (floor)** in Simulation Controls. Enable it and Apply while stopped. It uses FlyGym's unchanged trained proprioceptive/contact network and actual neck actuators; it defaults off. Loaded and unloaded gait, claw hold, remount, full-brain continuous clocks and pause passed. Unity scientific combat passed all 15 checks with this option and all 138639 neurons enabled. Evidence is in head-controller-parity.json, head-walking-evaluation.json, head-idle-stream-evaluation.json, head-walking-controller-evaluation.json and scientific-combat-head-full-brain-evaluation.json.

The trained head system is restricted to floor WalkingLab. Same-body wing landing and walking resumption are still qualification experiments in evaluate_unified_policies.py; they are not enabled as a playable unified mode. Fast gameplay, FlyBody wing flight and NeuroMechFly walking remain distinct modes.
