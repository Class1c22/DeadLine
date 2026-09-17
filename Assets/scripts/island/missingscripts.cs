using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Покласти цей файл у будь-яку папку "Editor" в проєкті
// (наприклад Assets/Editor/FindMissingScripts.cs).
// Після компіляції з'явиться пункт меню:
// Tools -> Find Missing Scripts In Scene

public static class FindMissingScripts
{
    [MenuItem("Tools/Find Missing Scripts In Scene")]
    public static void FindInScene()
    {
        int goCount = 0;
        int missingCount = 0;

        var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        var offenders = new System.Collections.Generic.List<GameObject>();
        var report = new System.Text.StringBuilder();

        foreach (var t in allTransforms)
        {
            // Пропускаємо об'єкти, що не належать поточній відкритій сцені
            // (асети, prefab-стейдж тощо обробляються окремо нижче).
            if (t.gameObject.scene.IsValid() == false) continue;

            goCount++;
            var components = t.gameObject.GetComponents<Component>();

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    missingCount++;
                    string path = GetFullPath(t);
                    offenders.Add(t.gameObject);
                    report.AppendLine($"{path}  (slot #{i})");

                    // LogError гарантовано не ховається Collapse/фільтрами так,
                    // як буває з LogWarning, і завжди клікабельний.
                    Debug.LogError($"Missing script на об'єкті: {path} (slot #{i})", t.gameObject);
                }
            }
        }

        Debug.Log($"Перевірено об'єктів: {goCount}. Знайдено missing scripts: {missingCount}.");

        if (offenders.Count > 0)
        {
            // Одразу виділяємо всі проблемні об'єкти в Hierarchy,
            // щоб їх не довелось шукати вручну по консолі.
            Selection.objects = offenders.ToArray();
            EditorGUIUtility.PingObject(offenders[0]);

            EditorUtility.DisplayDialog(
                "Знайдено missing scripts",
                report.ToString(),
                "OK");
        }
        else
        {
            EditorUtility.DisplayDialog(
                "Missing scripts",
                "Не знайдено жодного (у поточній відкритій сцені).",
                "OK");
        }
    }

    private static string GetFullPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}