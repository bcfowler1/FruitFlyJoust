using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
static class AutoCombatCheck
{
    static AutoCombatCheck(){EditorApplication.delayCall+=Run;}
    static void Run()
    {
        string once=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research/run-combat-check.once"));
        string active=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research/run-combat-check.active"));
        string enemyOnce=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research/run-enemy-check.once"));
        string enemyActive=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research/run-enemy-check.active"));
        if(EditorApplication.isPlaying)
        {
            if(File.Exists(enemyActive)){File.Delete(enemyActive);CombatTools.RunEnemyChecks();return;}
            if(File.Exists(active)){File.Delete(active);CombatTools.RunChecks();}
            return;
        }
        if(File.Exists(enemyOnce) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(enemyOnce);File.WriteAllText(enemyActive,"run");EditorSceneManager.OpenScene("Assets/CombatEncounter.unity");EditorApplication.isPlaying=true;return;
        }
        if(!File.Exists(once) || EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(once);File.WriteAllText(active,"run");EditorSceneManager.OpenScene("Assets/CombatLab.unity");EditorApplication.isPlaying=true;
    }
}
 
 
