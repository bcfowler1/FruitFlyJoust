"""Count distinct physical claw contacts, excluding body/wing collisions."""
import mujoco
def claw_contacts(model,data):
    feet=set();other=0
    for contact in data.contact:
        a,b=int(contact.geom1),int(contact.geom2)
        ground_a=int(model.geom_bodyid[a])==0;ground_b=int(model.geom_bodyid[b])==0
        if ground_a==ground_b:continue
        geom=b if ground_a else a
        name=(model.id2name(geom,'geom') if hasattr(model,'id2name') else
              mujoco.mj_id2name(model,mujoco.mjtObj.mjOBJ_GEOM,geom)) or ''
        if 'tarsal_claw_' in name and name.endswith('_collision'):feet.add(name)
        else:other+=1
    return feet,other
