"""Create the Unity visual wing cycle from FlyBody's measured FMech pattern.

The source is the GPL-3.0-or-later NeuroMechFly/FlyBody flight asset published at
https://doi.org/10.25378/janelia.25309105.  The three columns are wing yaw,
roll, and pitch in radians.  Steering gains remain declared gameplay animation
parameters because the recorded-reference dataset does not qualify a control
input-to-turn mapping.
"""
from pathlib import Path
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "work/research/flight-assets/wing_pattern_fmech.npy"
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/Resources/FlyWingAnimationProfile.json"
SAMPLES = 64

pattern = np.load(SOURCE)
phase_source = np.linspace(0.0, 1.0, len(pattern) + 1)
closed = np.vstack((pattern, pattern[0]))
phase_target = np.linspace(0.0, 1.0, SAMPLES, endpoint=False)
sampled = np.column_stack([
    np.interp(phase_target, phase_source, closed[:, axis]) for axis in range(3)
])

payload = {
    "source": "https://doi.org/10.25378/janelia.25309105",
    "source_asset": "wing_pattern_fmech.npy",
    "source_license": "GPL-3.0-or-later",
    "source_frequency_hz": 218.0,
    # Rendering a 218 Hz cycle directly aliases at ordinary display frame rates.
    # Preserve measured phase/angles but use this stroboscopic display rate.
    "display_frequency_hz": 31.0,
    "angles_degrees": np.degrees(sampled).round(5).reshape(-1).tolist(),
    "turn_differential": 0.22,
    "bank_differential": 0.14,
    "turn_plane_degrees": 9.0,
    "bank_plane_degrees": 13.0,
}
OUTPUT.write_text(json.dumps(payload, indent=2) + "\n")
print(OUTPUT)
