# Flight-controller evaluation and coupling contract

## Measured result

The pinned FlyBody flight imitation task loads and actuates six wing joints. Its default action vector has 12 entries; wingbeat-frequency modulation is one user action, while the task adds a baseline wing pattern to wing residual controls. The physics timestep is 50 microseconds and the control timestep is 200 microseconds. Flight uses a different configured task model from the passive floor.xml currently streamed to Unity; the passive viewer must not be relabelled as flight merely by changing its HUD.

evaluate_flight.py tested frequency requests -1, 0, and +1 (207.1, 218, and 228.9 Hz) with a fixed seed, the upstream approximate pattern, and zero learned-policy residual. Each run kept finite positions/velocities and moved the wings by more than two radians. With a longer synthetic reference, all three runs terminated after 53.2–53.6 ms when thorax height dropped below the task's 0.2 cm threshold. Acceleration norms stayed below its separate termination limit; the reference had not ended. These are mechanics checks, not successful flight-controller checks. Full measurements are in flight-evaluation.json. Initial short runs ended at the default reference boundary, so that boundary was extended before drawing the height-loss conclusion.

The [FlyBody paper](https://www.nature.com/articles/s41586-025-09029-4) provides trained controller networks and measured baseline wing data through its [Figshare dataset](https://doi.org/10.25378/janelia.25309105). Those assets were downloaded and tested on September 17, 2026; see TRAINED_FLIGHT.md. The published learned controller uses TensorFlow/Acme and is separate from the PyTorch connectome brain.

## Inputs and outputs

brain_body_interface.py defines and validates the first interface boundary:

| Layer | Signal | Meaning/status |
| --- | --- | --- |
| Rider | turn, climb in [-1,1]; spur/brake press events; land hold | Gameplay requests. Positive climb corresponds to left stick back. Press events retain the burst/cruise and braking requests; these are not neural firing rates. |
| Body feedback | body pose, linear/angular velocity, acceleration, joint state, contacts | Available physics information. Express transport positions in meters and time in simulated seconds; record frames for vectors. It still needs a biological sensory encoder. |
| Sensory encoder | versioned identified sensory populations, stimulus rates | Requires evidence and calibrated mappings. Reins need an explicit hypothetical mechanosensory model; no known biological rider/rein input is assumed. Sugar stimulation remains a diagnostic, not flight throttle. |
| Brain readout | population spike counts over a simulation-time window | Root IDs are decimal strings to preserve 64-bit precision. Validate identity against the v783 model and provenance against annotation data. Mean population rate = total spikes / population size / elapsed simulated seconds. |
| Flight decoder | flight power, steering, pitch/posture request | Disabled until verified populations and a tested rate-to-controller calibration exist. Do not derive these from total whole-brain spikes. |
| Body controller | trained policy actions plus wingbeat generator | Run at the task's 200 microsecond control timestep, with 50 microsecond physics integration. The 15 ms viewer/brain exchange window is distinct from wing control and must be benchmarked for delays. |
| Unity | body poses, rider attachment, independent look/aim, combat | Presentation/gameplay. Rider currently has no mass or forces in the scientific body model; those need an explicit physical scale and later testing. |

The public FlyWire 783 annotation dump has now been downloaded separately and pinned to a recorded commit/SHA-256. Twenty-five DNg02 candidate root IDs match the neural model (flight-populations.json). Optional CUDA counters measure each neuron's spikes; the counting diagnostic passed. Membership and cell-type evidence do not provide a validated flight decoder. See FLIGHT_PROGRESS.md and flight-readout-evaluation.json.

The DNg02 population is a candidate for flight-power investigation: [Namiki et al.](https://pubmed.ncbi.nlm.nih.gov/35090590/) report regulation of wingbeat amplitude. This does not supply a numeric rate-to-speed calibration or identify matching v783 root IDs here. In particular, amplitude regulation must not be silently equated with the frequency-control action available in the FlyBody task. Flight steering and pitch populations remain unselected. Eon's [embodiment description](https://eon.systems/updates/embodied-brain-emulation) describes a selected descending-neuron interface to lower-level controllers, with walking/feeding/grooming demonstrations; its walking readouts are not evidence of a flight controller.

## Next gate

September 17 update: the user specified a one-third-mass rider and authorized completing integration. Loaded flight and an opt-in experimental connectome feedback loop have now executed. ExperimentalEncoder explicitly applies artificial excitation to descending DNg02 cells; it is not a sensory-neuron encoder. Actual counted spikes add small wing-yaw residuals; the trained checkpoint provides stabilization and pitch. This bypasses the biological calibration gate only as a labeled engineering experiment, without changing require_flight_calibration or its refusal checks. Six-claw contact physics and enemy gameplay are separate experiments. See FLIGHT_PROGRESS.md for current results; the paragraphs below describe the earlier baseline milestone.

Unity integration, full recorded-reference checks, declared velocity disturbances, version-matched DNg02 identities, population counting, and a compiled inference wrapper are implemented. Next, calibrate population readouts against wing kinematics and supply pitch/steering and sensory mappings. No decoder is enabled. Preserve the user's rider controls as requests throughout; do not bypass the scientific brain with joystick forces and claim neural flight.

This milestone finished the baseline flight-task evaluation and interface definition. It did not implement neural flight, sensory feedback, rider loads, wall adhesion, or combat.
