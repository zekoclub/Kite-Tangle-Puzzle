using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates the Stage 1 game scene. Menu: Kite Tangle > Build Game Scene (also used from batchmode).
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";

    [MenuItem("Kite Tangle/Build Game Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.82f, 0.98f);

        new GameObject("TangleBoard").AddComponent<TangleBoard>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "Kite Tangle";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneBuilder] Built " + ScenePath);
    }
}
