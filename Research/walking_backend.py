"""FlyGym 1.1 detailed walking body; mm positions, native scaled mass units."""
import gzip,json
from pathlib import Path
import mujoco,numpy as np
from flygym import Fly
from flygym.examples.locomotion import HybridTurningController

class WalkingBody:
    def __init__(self,optimized=False,head_stabilization=False):
        sensors=[leg+segment for leg in ('LF','LM','LH','RF','RM','RH')
            for segment in ('Tibia','Tarsus1','Tarsus2','Tarsus3','Tarsus4','Tarsus5')]
        self.head_controller=None
        if head_stabilization:
            from head_controller import HeadController
            self.head_controller=HeadController()
        self.fly=Fly(enable_adhesion=True,contact_sensor_placements=sensors,
            head_stabilization_model=self.head_controller,neck_kp=500 if head_stabilization else None)
        self.sim=HybridTurningController(fly=self.fly,cameras=[],timestep=.0001,seed=42)
        self.obs,_=self.sim.reset(seed=42)
        self.m=self.sim.physics.model.ptr;self.d=self.sim.physics.data.ptr
        self.anchor=mujoco.mj_name2id(self.m,mujoco.mjtObj.mjOBJ_BODY,self.fly.name+'/Thorax')
        if self.anchor<0:raise RuntimeError('Thorax body absent')
        mass=float(self.m.body_mass.sum());rider=mass/3
        self.base_thorax_mass=float(self.m.body_mass[self.anchor])
        self.base_thorax_inertia=self.m.body_inertia[self.anchor].copy()
        self.rider_mass=rider;self.rider_attached=True
        self.load=dict(fly_mass_native=mass,rider_mass_native=rider,mass_fraction=1/3,
            mass_units='upstream scaled MuJoCo units; physical conversion not asserted',
            attachment='rigid thorax load; isotropic small-rider inertia approximation')
        # Preserve upstream mass scaling. Add rigid inertia in native mass*mm^2.
        self.m.body_mass[self.anchor]+=rider
        self.m.body_inertia[self.anchor]+=rider*.1**2
        mujoco.mj_setConst(self.m,self.d)
        self.observation_cache={}
        if optimized:
            from walking_observation_cache import install
            install(self)
        self.geoms=[i for i in range(self.m.ngeom) if self.m.geom_type[i]==mujoco.mjtGeom.mjGEOM_MESH and self.m.geom_bodyid[i]!=0]
        self.reset()
    def set_rider_attached(self,attached):
        if type(attached) is not bool:raise ValueError('Attachment must be boolean')
        if attached==self.rider_attached:return
        saved={name:getattr(self.d,name).copy() for name in ('qpos','qvel','act','ctrl','qacc_warmstart')}
        clock=float(self.d.time)
        rider=self.rider_mass if attached else 0.
        self.m.body_mass[self.anchor]=self.base_thorax_mass+rider
        self.m.body_inertia[self.anchor]=self.base_thorax_inertia+rider*.1**2
        mujoco.mj_setConst(self.m,self.d)
        for name,value in saved.items():getattr(self.d,name)[:]=value
        self.d.time=clock;mujoco.mj_forward(self.m,self.d)
        self.observation_cache.clear();self.rider_attached=attached
        self.load['rider_mass_native']=rider;self.load['mass_fraction']=1/3 if attached else 0.
        self.load['configured_mass_fraction']=1/3
    def reset(self):
        self.observation_cache.clear()
        self.obs,_=self.sim.reset(seed=42)
        self.ended=False
        self.d.xfrc_applied[:]=0
        self.d.qfrc_applied[:]=0
    def feedback(self):
        forward=self.obs['fly_orientation']
        return dict(speed_mmps=float(np.linalg.norm(self.obs['fly'][1][:2])),
            yaw_rate_radps=-float(self.obs['fly'][3][2]),contacting_claws=len(self.contacts()))
    def contacts(self):
        ground=mujoco.mj_name2id(self.m,mujoco.mjtObj.mjOBJ_GEOM,'ground');legs=set()
        for contact in self.d.contact:
            if ground not in (contact.geom1,contact.geom2):continue
            other=contact.geom2 if contact.geom1==ground else contact.geom1
            name=mujoco.mj_id2name(self.m,mujoco.mjtObj.mjOBJ_BODY,int(self.m.geom_bodyid[other])) or ''
            if 'Tarsus5' in name:legs.add(name)
        return legs
    def step(self,action):
        if self.ended:return
        for _ in range(150):
            self.obs,_,terminated,truncated,_=self.sim.step(np.asarray(action,dtype=float))
            if not np.isfinite(self.d.qpos).all() or not np.isfinite(self.d.qvel).all():raise RuntimeError('Nonfinite body state')
            self.ended=bool(terminated or truncated)
            if self.ended:break
    def export(self):
        meshes=[]
        for index in sorted(set(int(self.m.geom_dataid[g]) for g in self.geoms)):
            va,vn=self.m.mesh_vertadr[index],self.m.mesh_vertnum[index]
            fa,fn=self.m.mesh_faceadr[index],self.m.mesh_facenum[index]
            meshes.append(dict(id=index,vertices=(self.m.mesh_vert[va:va+vn]*.001).ravel().tolist(),triangles=self.m.mesh_face[fa:fa+fn].ravel().tolist()))
        geoms=[]
        for g in self.geoms:
            mat=self.m.geom_matid[g];color=(self.m.mat_rgba[mat] if mat>=0 else self.m.geom_rgba[g]).copy()
            # Source body pigments live in MuJoCo cube textures, not rgba.
            # Bake their mean pigment into the UV-free Unity mesh material.
            # This preserves color; source cube-texture patterns are not exported.
            if mat>=0:
                texture=int(self.m.mat_texid[mat,1])
                if texture>=0:
                    channels=int(self.m.tex_nchannel[texture]);start=int(self.m.tex_adr[texture])
                    count=int(self.m.tex_width[texture]*self.m.tex_height[texture])*channels
                    pixels=self.m.tex_data[start:start+count].reshape(-1,channels)
                    color[:3]*=pixels[:,:3].mean(axis=0)/255
            geoms.append(dict(name=mujoco.mj_id2name(self.m,mujoco.mjtObj.mjOBJ_GEOM,g),mesh=int(self.m.geom_dataid[g]),rgba=color.tolist()))
        target=Path(__file__).resolve().parents[1]/'Assets/Resources/FlyWalkingGeometry.bytes'
        target.write_bytes(gzip.compress(json.dumps(dict(meshes=meshes,geoms=geoms,units='meters',
            source='NeLy-EPFL/flygym 1.1.0 NeuroMechFly v2',license='Apache-2.0',texture_pigment_baked=True,texture_patterns_exported=False)).encode(),mtime=0))
        return dict(meshes=len(meshes),geoms=len(geoms),path=str(target))
    def packet(self,session,seq,paused,elapsed,brain=None):
        quats=[]
        for g in self.geoms:
            q=np.empty(4);mujoco.mju_mat2Quat(q,self.d.geom_xmat[g]);quats.extend(q.tolist())
        stats=(brain or {}).get('stats',{})
        return dict(protocol=1,session=session,seq=seq,sim_time=float(self.d.time),
            positions=(self.d.geom_xpos[self.geoms]*.001).ravel().tolist(),rotations=quats,
            anchor_position=(self.d.xpos[self.anchor]*.001).tolist(),anchor_rotation=self.d.xquat[self.anchor].tolist(),
            paused=paused,episode_ended=self.ended,episode_status='terminated' if self.ended else 'running',
            neural_time=(brain or {}).get('neural_time',0),neurons=stats.get('neurons',0),
            connections=stats.get('directed_connection_rows',0),spikes=stats.get('spike_count',0),active=stats.get('active_neurons',0),
            compute_ratio=0 if paused else .015/max(elapsed,1e-6),rider_load=self.load,
            walking_lab=True,body_controller='FlyGym hybrid walking; synthetic descending interface; not Eon reproduction')
    def close(self):self.sim.close()
