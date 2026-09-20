# Fly simulation and rider game

The target is a biologically grounded Drosophila mount in a fantasy rider game. The player can dismount for melee and archery, and use archery or jousting while mounted. Rider controls and combat must remain separate from the fly's sensory inputs, neural control, body mechanics, and contact model. Rider/reins and current button effects are game mechanics, not evidence about fly biology.

Confirmed rider rule: while mounted, the rider grips the fly with his legs and remains attached regardless of orientation, including inverted ceiling perches. Orientation, banking, gravity, and landing do not automatically dismount him. Dismounting must be a deliberate player action. The existing rider visuals inherit the fly/body transform, so wall and ceiling rotations already carry the rider with the mount. Future rider animation and mounted melee/archery/jousting must preserve this attachment rule. This rule does not decide rider mass or the forces applied to the fly; those remain separate modeling choices.

The present Unity build is a gameplay prototype. It is not a completely realistic fly simulation. Direct velocity control, a single capsule, primitive wings/legs, fixed speed cues, unlimited attachment, and heuristic obstacle avoidance are approximations. A connectome map alone does not provide a complete validated neural controller, muscle model, or aerodynamic model.

## Research backend

The brain model is a core integration requirement, not optional polish after a conventional game. The user's reference to popular embodied-brain work likely points to [Eon's current integration](https://eon.systems/updates/embodied-brain-emulation): a FlyWire-derived Shiu-style leaky-integrate-and-fire brain, a connectome-constrained visual model, and NeuroMechFly in MuJoCo. This identification is a working inference; the exact backend/release must be selected and verified. Eon's [September 2026 explanation](https://eon.systems/updates/more-flies-are-getting-uploaded) describes selected descending neural activity driving body-level controllers rather than individual motor neurons. Preserve that distinction and do not claim an entirely biologically validated brain-to-muscle system. No such neural model is currently running in our Unity build.

Preferred starting point for flight/body evaluation: [FlyBody, Turaga Lab](https://github.com/TuragaLab/flybody), an anatomically detailed Drosophila body for MuJoCo, with walking and flight task environments. See its [2025 whole-body physics paper](https://www.nature.com/articles/s41586-025-09029-4). Evaluate its actual flight controller and aerodynamic assumptions before claiming validation; trained task policies do not automatically implement an entire biological brain.

For contact, sensing, walking, and adhesion: [NeuroMechFly / FlyGym](https://neuromechfly.org/) and its [v2 methods paper](https://www.nature.com/articles/s41592-024-02497-y). Its documentation describes articulated anatomy, vision, odor sensing, proprioception, and leg adhesion, while identifying adhesion switching as an abstraction. FlyGym's new 2.x API differs from 1.x; select and pin a specific tested release before integrating it.

Architecture: Unity owns the gamepad, rider, weapons, world presentation, and rendering. A simulation backend owns the articulated fly, physics, contacts, and sensorimotor state. Exchange body/joint poses, surface/contact information, environmental stimuli, and rider cue requests with timestamps. The backend's physics clock must not depend on rendered frame rate. Convert units and coordinate handedness explicitly. Avoid advancing the same body with both Unity velocity control and MuJoCo physics.

## Implementation sequence

1. Evaluate the research model locally using isolated environments and pinned revisions. Initial independent smoke tests are complete: the full released brain runs on CPU with sensory stimulation, and FlyBody loads/steps/renders in MuJoCo. The research packages are installed in a workspace environment; neither is integrated into Unity yet. See Research/README.md and the evaluation JSON files. Locomotion-policy tests and biological validation remain outstanding.
2. Establish morphology, mass/inertia, physical scale, environmental gravity, wing actuation/aerodynamics, and six articulated leg contacts. Replace visual-only wings and primitive legs.
3. Bridge the body's state into Unity and verify coordinate conversions and synchronization. Retain the current ride as a comparison baseline until the new backend is functional.
4. Add measured or calibrated sensory feedback and neural controllers. Validate speed, turning, landing, adhesion, gait, and takeoff against Drosophila data. Mark inferred or unvalidated parameters separately.
5. Implement rider mounting/dismounting and combat. Rider mass, forces through the saddle/reins, and dismount impulses should enter the physical model if rider load is enabled; a massless presentation rider is a separate explicit gameplay mode. Biological carrying capacity for a fantasy rider has not been established.

## Current surface implementation

The Unity prototype now probes floors, walls, ceilings, slopes, and the movement direction. It checks a four-point footprint, rotates the feet toward the surface, perches at slow close contact, follows the supporting transform, and launches outward along the surface normal. The camera follows surface orientation smoothly. This is a geometric attachment approximation: it does not calculate adhesive forces, individual tarsal contacts, leg articulation, material effects, or measured landing kinematics. A validation menu checks orientation math, but physical play testing is still required.
