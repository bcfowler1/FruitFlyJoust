# Additional flight and landing validation — September 18, 2026

The original additional checks exposed actuator saturation and higher-landing instability. The neutral-head correction now passes all twelve short-reference neural flight trials; higher-landing recovery is still being tested. The previously qualified controlled floor-loop Unity mode is unchanged.

## Neutral-head correction

`flight-saturation-diagnostic-20260918.json` isolates every clipped entry to the published Gaussian head command, including unloaded baseline flight. `flight_motor_controller.py` now commands a neutral native neck position through its existing position actuators, retaining trained wing/abdomen/frequency outputs and the experimental wing residual. This is a declared engineering controller, not biological head control. All twelve trials in `neural-flight-causality-20260918T223415Z-evaluation.json` pass unchanged height, tracking, duration and zero-clipping limits; enabled repeats match exactly and readout ablation still changes physical outcomes. The same function is integrated only into experimental live flight, whose transport check passed on trajectory2 with 14 actual frames, zero clipping and matched clocks. Historical failures below are preserved.

## Actual spike-readout motor influence

`neural-flight-causality-20260918T220138Z-evaluation.json` contains twelve physical trials: residual disabled, enabled, zero-readout ablation and enabled repeat for each of three recorded flight references. Actual full-connectome counted rates are decoded into small wing residuals; the trained checkpoint remains the stabilizer. The one-third rider load is retained.

All three enabled physical outcomes differ from the disabled and zero-readout outcomes. Enabled repeats match exactly (maximum final qpos difference zero), and body/neural clocks agree to approximately 1.7e-14 seconds. This establishes repeatable spike-readout-dependent influence through the declared decoder. It does not establish dependence on upstream synaptic pathways: descending cells receive direct synthetic excitation, and no upstream-network ablation was performed.

Existing flight limits are at least .1 seconds, thorax height at least .002 meters, tracking error below .02 meters and no raw action clipping. Trajectories0 and1 pass the complete test. Trajectory2 has 21 raw action clips with residual disabled and enabled; its zero-readout ablation has additional clipping. Overall engineering qualification is false. Actual references last only .1892, .227 and .2058 seconds, so these tests do not establish sustained free-flight control, rein decoding or biological motor calibration. Earlier on/off-only results remain saved separately.

The DNg02 candidate is supported by [Namiki et al., A population of descending neurons that regulate the flight motor of Drosophila](https://pmc.ncbi.nlm.nih.gov/articles/PMC9206711/), which reports regulation of wingbeat amplitude. That does not validate our arbitrary rate-to-wing residual gains or synthetic rider-feedback excitation. Experimental data and analysis code are linked by that paper at [Mendeley](https://doi.org/10.17632/7g984jm2zc.1). No empirical calibration against those data was completed in these checks.

## Varied physical landing

`unified-varied-hover025-20260918-evaluation.json` uses the unchanged qualified controller at a genuinely different .25 cm hover command above the loaded stance, compared with the qualified .15 cm command. The physical trajectory evolves continuously; no body pose, velocity, activation or clock reset is used within the transition. Hover passes with RMS tracking error .016030239 cm and attitude error .088379793 rad. Subsequent landing fails: no stable claw support is obtained within four simulation seconds; final up cosine .090599889 and speed31.958962 cm/s violate the unchanged original landing limits. Walking is not reached or qualified.

This counterexample rejects arbitrary-height landing robustness for the current controller. Five phase-aligned repetitions of the original controlled launch remain valid within their narrower scope.

## Next concrete work

For neural flight, diagnose the raw-action saturation on recorded trajectory2 before expanding duration or claiming engineering qualification; then test upstream-network dependence separately from direct descending excitation. For landing, inspect first claw contact and attitude/speed during the .25 cm descent, then qualify a distinct recovery candidate on varied physical trajectories before touching the live qualified source. Biological qualification additionally requires calibrated sensory/motor mapping and independent empirical comparison. No failed extension is enabled. All tests and their owned processes are stopped.

## Current landing candidates

`constant-descent025-evaluation.json` preserves the original limits but fails: slower descent still obtains only one/two claws and no valid grip. `evaluate_staged_descent.py` is a separate candidate currently running at .25 cm; it descends to the .15 cm landing command with legs tucked before beginning floor-reaching IK, preventing the legs from chasing unreachable floor targets during the higher approach. No failed landing candidate is enabled.
