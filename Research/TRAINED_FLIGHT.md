# Published flight-controller test

This milestone is finished. The published learned controller maintained altitude and tracked three short recorded flight trajectories using the measured wing pattern. It is now streamed to Unity's separate FlightLab research scene (see UNITY_FLIGHT.md). It is not linked to the connectome brain and includes no rider loads.

Assets came from the authors' [Figshare dataset](https://doi.org/10.25378/janelia.25309105): trained-fly-policies.zip and datasets_flight-imitation.zip. Both MD5 checksums matched their published values; SHA-256, URLs, sizes, and the GPL-3.0-or-later dataset license are recorded in flight-assets.json. The measured pattern contains 500 samples of three wing angles. Downloads/checkpoints remain in work/research/flight-assets; the body mesh's separate Apache license does not replace the dataset license.

Run Research/evaluate_trained_flight.py with work/flight-env/Scripts/python.exe. The separate environment uses Python 3.12, TensorFlow 2.16.1, TFP 0.24, NumPy 1.26.4, and MuJoCo 3.13.0; full local package versions are in flight-environment-freeze.txt. The older checkpoint's Independent distribution TypeSpec name is aliased to the current backward-compatible deserializer in memory. The checkpoint, weights, and graph operations are unchanged. This is a compatibility adaptation, not an exact reproduction of the authors' TensorFlow 2.8 runtime. No comparison with that original runtime has been made.

Tests use distribution-mean actions, seed 42, the measured wing pattern, three recorded reference trajectories, and at most 1,000 control steps per rollout. Actions are explicitly clipped to the task bounds before application. Body physics advances every 50 microseconds, and control updates every 200 microseconds.

| Recorded trajectory | Simulated duration | Mean tracking error | Lowest thorax height | Clipped action entries |
| --- | --- | --- | --- | --- |
| 0 | 189.2 ms; reference completed | 0.496 mm | 5.389 mm | 0 |
| 1 | 200 ms; test limit | 0.400 mm | 5.367 mm | 0 |
| 2 | 200 ms; test limit | 0.306 mm | 5.366 mm | 3 |

All state values remained finite. The largest tracking error was 2.509 mm. Repeating trajectory 0 reproduced its final joint/root state within the specified tolerance. The zero-policy comparison, using the same measured pattern and trajectory 0, fell below the 2 mm minimum-height threshold after 57.2 ms.

Trajectory 2 exceeded the head_abduct action bound at three time steps, by at most 0.007303 radians (about 0.42 degrees). The bounded wrapper handled these values, and flight remained stable over this short test. The original strict acceptance rule included **no clipping**, so trained-flight-evaluation.json correctly reports **controller criteria not met** rather than an unconditional pass. These tests support a short-flight result with bounded actions, not a guarantee across all trajectories or disturbances.

Each roughly 0.2-second trained rollout took about 3.5 seconds on CPU (around 0.06 times real time), excluding checkpoint loading. The test saved trained-flight-preview.png from trajectory 1; orange is the simulated fly and gray is the reference ghost. It is a MuJoCo test image, not Unity gameplay.

Unity streaming and action-clip monitoring are implemented. Next research work: test longer reference paths and disturbances, and investigate annotated flight-neuron populations, sensory encoding, and validated decoder calibration. These are required for scientific neural flight coupling.
