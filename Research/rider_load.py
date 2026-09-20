"""Rigid miniature rider inertia in FlyBody's gram/centimeter unit system."""
import numpy as np
import mujoco

class RiderLoad:
    def __init__(self, physics, fraction=1/3, offset_cm=(0, 0, .05), radius_cm=.025):
        if not np.isfinite(fraction) or not 0 <= fraction <= 1:
            raise ValueError('Rider mass fraction must lie within [0, 1]')
        model = physics.model.ptr
        self.anchor = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'walker/thorax')
        if self.anchor < 0: self.anchor = mujoco.mj_name2id(model, mujoco.mjtObj.mjOBJ_BODY, 'thorax')
        if self.anchor < 0: raise ValueError('Thorax not found')
        bodies=[]
        for i in range(model.nbody):
            ancestor=i
            while ancestor and ancestor != self.anchor: ancestor=int(model.body_parentid[ancestor])
            if ancestor == self.anchor: bodies.append(i)
        self.fly_mass_g = float(model.body_mass[bodies].sum())
        self.mass_g = self.fly_mass_g * fraction
        self.offset = np.asarray(offset_cm, dtype=float)
        if self.offset.shape != (3,) or not np.isfinite(self.offset).all() or radius_cm <= 0:
            raise ValueError('Invalid rider geometry')
        i = self.anchor
        self.base_mass = float(model.body_mass[i])
        self.base_position = model.body_ipos[i].copy()
        rotation = np.empty(9)
        mujoco.mju_quat2Mat(rotation, model.body_iquat[i])
        rotation = rotation.reshape(3, 3)
        self.base_tensor = rotation @ np.diag(model.body_inertia[i]) @ rotation.T
        self.radius = radius_cm

    def apply(self, physics):
        model, data = physics.model.ptr, physics.data.ptr
        mass = self.base_mass + self.mass_g
        position = (self.base_mass*self.base_position + self.mass_g*self.offset)/mass
        def shift(m, d): return m*(np.dot(d,d)*np.eye(3)-np.outer(d,d))
        tensor = self.base_tensor + shift(self.base_mass,self.base_position-position)
        tensor += .4*self.mass_g*self.radius**2*np.eye(3) + shift(self.mass_g,self.offset-position)
        values, axes = np.linalg.eigh(tensor)
        if np.linalg.det(axes) < 0: axes[:,0] *= -1
        quat = np.empty(4); mujoco.mju_mat2Quat(quat, axes.ravel())
        model.body_mass[self.anchor] = mass
        model.body_ipos[self.anchor] = position
        model.body_inertia[self.anchor] = values
        model.body_iquat[self.anchor] = quat
        # mj_setConst evaluates the reference configuration. Preserve the live state.
        saved = {name: getattr(data,name).copy() for name in ('qpos','qvel','act','ctrl','qacc_warmstart')}
        saved_time = data.time
        mujoco.mj_setConst(model, data)
        for name, value in saved.items(): getattr(data,name)[:] = value
        data.time = saved_time
        physics.forward()
        physics_mass = self.fly_mass_g+self.mass_g
        return dict(fly_mass_mg=self.fly_mass_g*1000, rider_mass_mg=self.mass_g*1000,
            mass_fraction=self.mass_g/self.fly_mass_g, total_mass_mg=physics_mass*1000,
            attachment='rigid thorax inertia; spherical rider approximation', offset_cm=self.offset.tolist())
