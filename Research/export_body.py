"""Export compiled upstream visual geometry for the Unity research viewer."""
import json
import gzip
from pathlib import Path
import mujoco
from evaluate_backend import ROOT, RESEARCH

OUTPUT = ROOT / 'outputs' / 'FruitFlyJoust' / 'Assets' / 'Resources' / 'FlyResearchGeometry.bytes'

def body_model():
    model = mujoco.MjModel.from_xml_path(str(RESEARCH / 'flybody' / 'flybody' / 'fruitfly' / 'assets' / 'floor.xml'))
    data = mujoco.MjData(model)
    mujoco.mj_forward(model, data)
    return model, data

def visible_geoms(model):
    return [i for i in range(model.ngeom) if model.geom_type[i] == mujoco.mjtGeom.mjGEOM_MESH
        and model.geom_bodyid[i] != 0 and model.geom_group[i] == 1]

def main():
    model, data = body_model()
    geom_ids = visible_geoms(model)
    export_geometry(model, geom_ids, OUTPUT)

def export_geometry(model, geom_ids, output):
    meshes = []
    for mesh_id in sorted(set(int(model.geom_dataid[i]) for i in geom_ids)):
        va, vn = model.mesh_vertadr[mesh_id], model.mesh_vertnum[mesh_id]
        fa, fn = model.mesh_faceadr[mesh_id], model.mesh_facenum[mesh_id]
        meshes.append(dict(id=mesh_id, vertices=[round(float(v), 9) for v in (model.mesh_vert[va:va+vn] * .01).ravel()],
            triangles=model.mesh_face[fa:fa+fn].ravel().tolist()))
    geoms = []
    for i in geom_ids:
        material = model.geom_matid[i]
        color = model.mat_rgba[material] if material >= 0 else model.geom_rgba[i]
        geoms.append(dict(name=mujoco.mj_id2name(model, mujoco.mjtObj.mjOBJ_GEOM, i) or f'geom_{i}',
            mesh=int(model.geom_dataid[i]), rgba=color.tolist()))
    output.parent.mkdir(exist_ok=True)
    payload = json.dumps(dict(meshes=meshes, geoms=geoms,
        units='meters', coordinates='right-handed Z up',
        source='TuragaLab/flybody d015e9bfe441bd90ae431bac24c55cb74bdbce26',
        license='Apache-2.0'), separators=(',', ':')).encode('utf-8')
    output.write_bytes(gzip.compress(payload, mtime=0))
    print(json.dumps(dict(path=str(output), meshes=len(meshes), visual_geoms=len(geoms), bytes=output.stat().st_size)))

if __name__ == '__main__': main()
