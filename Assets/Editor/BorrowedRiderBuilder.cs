using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BorrowedRiderBuilder
{
    const string Folder = "Assets/BorrowedRider/";
    static AnimationClip Clip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(Folder + name).OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview"));
    }
    [MenuItem("Fruit Fly/Build Borrowed Rider")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "AlteredAndroid.fbx");
        var rig = AssetDatabase.LoadAllAssetsAtPath(Folder + "Armature.fbx").OfType<Avatar>().First(a => a.isHuman && a.isValid);
        var idle = Clip("Stand--Idle.anim.fbx"); var walk = Clip("Locomotion--Walk_N.anim.fbx");
        var ride = Clip("Rider_Idle_01.FBX"); var sword = Clip("SaberSlash.fbx"); var bow = Clip("ShootingArrow.fbx");
        var transitionClips=AssetDatabase.LoadAllAssetsAtPath(Folder+"Rider_Mount_Dismount_Left.FBX").OfType<AnimationClip>();
        var mount=transitionClips.First(c=>c.name=="Rider_Mount_Left");
        var dismount=transitionClips.First(c=>c.name=="Rider_Dismount_Left");
        const string path = "Assets/Resources/BorrowedRider.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (var layer in controller.layers) foreach (var child in layer.stateMachine.states) layer.stateMachine.RemoveState(child.state);
        var machine = controller.layers[0].stateMachine;
        foreach (var pair in new[] { ("Idle", idle), ("Walk", walk), ("Ride", ride), ("Mount",mount), ("Dismount",dismount) })
        { var state = machine.AddState(pair.Item1); state.motion = pair.Item2; }
        machine.defaultState = machine.states.First(s => s.state.name == "Ride").state;
        if (controller.layers.Length < 2) controller.AddLayer("Combat");
        var layers = controller.layers; layers[0].iKPass = true; var upper = layers[1]; upper.defaultWeight = 1;
        var mask = upper.avatarMask;
        if (!mask) { mask = new AvatarMask(); mask.name = "Rider Upper Body"; AssetDatabase.AddObjectToAsset(mask, controller); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
            i != (int)AvatarMaskBodyPart.Root && i != (int)AvatarMaskBodyPart.LeftLeg && i != (int)AvatarMaskBodyPart.RightLeg && i != (int)AvatarMaskBodyPart.LeftFootIK && i != (int)AvatarMaskBodyPart.RightFootIK);
        upper.avatarMask = mask; layers[1] = upper; controller.layers = layers;
        var empty = upper.stateMachine.AddState("Ready"); upper.stateMachine.defaultState = empty;
        foreach (var pair in new[] { ("Sword", sword), ("Bow", bow) })
        {
            var state = upper.stateMachine.AddState(pair.Item1); state.motion = pair.Item2;
            var exit = state.AddTransition(empty); exit.hasExitTime = true; exit.exitTime = .95f; exit.duration = .1f;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var animator = instance.GetComponent<Animator>(); if (!animator) animator = instance.AddComponent<Animator>();
        animator.avatar = rig; animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
        animator.fireEvents=false;
        if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0) { UnityEngine.Object.DestroyImmediate(instance); throw new InvalidOperationException("Borrowed rider model has no skinned mesh"); }
        PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Resources/BorrowedRider.prefab");
        UnityEngine.Object.DestroyImmediate(instance); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        Debug.Log("BORROWED_RIDER_READY: humanoid riding, walk, sword and archery clips; root motion off, mounted legs masked from attacks");
    }
}
