"""Capture one real airborne pose from the qualified unified fly simulation.

The output is thorax-relative so the playable visual can use its own root frame
without guessing per-leg rotations.
"""
import json
from pathlib import Path

import mujoco
from dm_control import mjcf
import evaluate_unified_policies as evaluation
from export_body import visible_geoms

ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT.parent / "Assets/Resources/FlyUnifiedFlightPose.json"


class Captured(Exception):
    pass


def playable_name(name):
    # Unified T1/T2/T3 correspond to playable front/middle/hind.
    prefix = {"left": "L", "right": "R"}
    pair = {"T1": "F", "T2": "M", "T3": "H"}
    segment = {
        "coxa": "Coxa", "femur": "Femur", "tibia": "Tibia",
        "tarsus": "Tarsus1", "tarsus2": "Tarsus2",
        "tarsus3": "Tarsus3", "tarsus4": "Tarsus4",
        "tarsal_claw": "Tarsus5",
    }
    short = name.removeprefix("walker/")
    for side in prefix:
        for station in pair:
            suffix = "_" + station + "_" + side
            if short.endswith(suffix):
                stem = short[:-len(suffix)]
                if stem in segment:
                    return "0/" + prefix[side] + pair[station] + segment[stem]
    return None


def sample(physics):
    model, data = physics.model, physics.data
    anchor = model.name2id("walker/thorax", "body")
    anchor_pos = data.xpos[anchor].copy()
    anchor_quat = data.xquat[anchor].copy()
    inverse = anchor_quat.copy(); mujoco.mju_negQuat(inverse, anchor_quat)
    entries = []
    for geom in visible_geoms(model.ptr):
        name = model.id2name(geom, "geom") or ""
        target = playable_name(name)
        if target is None:
            continue
        relative_position = data.geom_xpos[geom] - anchor_pos
        local_position = relative_position.copy()
        mujoco.mju_rotVecQuat(local_position, relative_position, inverse)
        world_rotation = inverse.copy()
        mujoco.mju_mat2Quat(world_rotation, data.geom_xmat[geom])
        local_rotation = inverse.copy()
        mujoco.mju_mulQuat(local_rotation, inverse, world_rotation)
        entries.append(dict(name=target, source=name,
            position=(local_position * .01).tolist(), rotation=local_rotation.tolist()))
    if len(entries) != 48:
        raise RuntimeError(f"expected 48 leg geoms, found {len(entries)}")
    return entries


def main():
    original_step = mjcf.Physics.step
    original_apply = evaluation.RiderLoad.apply
    holder = {"physics": None, "rest": None}
    def loaded(load, physics):
        result = original_apply(load, physics)
        holder["physics"] = physics.step.__self__
        return result
    def observed_step(physics, *args, **kwargs):
        result = original_step(physics, *args, **kwargs)
        if physics is holder["physics"] and holder["rest"] is None and physics.data.time >= .699:
            holder["rest"] = {e["name"]: e for e in sample(physics)}
        if physics is holder["physics"] and physics.data.time >= 1.55:
            entries=sample(physics)
            for entry in entries:
                rest=holder["rest"][entry["name"]]
                entry["rest_position"]=rest["position"]
                entry["rest_rotation"]=rest["rotation"]
            OUTPUT.write_text(json.dumps(dict(simulation_time=float(physics.data.time), rest_time=.699,
                anchor="walker/thorax", coordinate_units="meters", entries=entries), indent=2))
            print(json.dumps(dict(captured=str(OUTPUT), simulation_time=float(physics.data.time), entries=len(entries))))
            raise Captured()
        return result
    try:
        evaluation.RiderLoad.apply = loaded
        mjcf.Physics.step = observed_step
        evaluation.main(output_name="flight-pose-capture-unused.json", landing=True,
            upright_landing=False, stance_before_launch=True, leg_ik=True,
            claw_hold=True, resume_walking=True, seed=42, grip_ramp=.2,
            stance_ramp=.2, hold_seconds=2, filter_settle_seconds=.1,
            claw_release_seconds=0, walking_seconds=6, resume_speed=1,
            grip_max_speed=2, body_recovery=True, replant_feet=True,
            adaptive_grip=True, automatic_recovery=True,
            loaded_stance_handover=True, synchronize_wings=True,
            complete_contacts=True, walking_blend_seconds=0)
    except Captured:
        pass
    finally:
        mjcf.Physics.step = original_step
        evaluation.RiderLoad.apply = original_apply


if __name__ == "__main__":
    main()

