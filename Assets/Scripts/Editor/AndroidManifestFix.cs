using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

// Turns off Android auto-backup (android:allowBackup="false") in the generated Gradle project, so saves
// (coins, hints, progress) can't be pulled out with adb backup and edited. tools:replace wins over any
// library that sets it to true. Runs on every Android build.
public class AndroidManifestFix : IPostGenerateGradleAndroidProject
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";
    const string ToolsNs = "http://schemas.android.com/tools";

    public int callbackOrder => 0;

    // `path` is the unityLibrary module; the app's own manifest is in the launcher module next to it.
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        foreach (var module in new[] { path, Path.Combine(path, "..", "launcher") })
        {
            string manifest = Path.Combine(module, "src", "main", "AndroidManifest.xml");
            if (File.Exists(manifest)) Patch(manifest);
        }
    }

    static void Patch(string file)
    {
        var doc = new XmlDocument();
        doc.Load(file);
        var root = doc.DocumentElement;
        if (root == null) return;
        if (!root.HasAttribute("xmlns:tools")) root.SetAttribute("xmlns:tools", ToolsNs);

        if (root.SelectSingleNode("application") is not XmlElement app)
        {
            app = doc.CreateElement("application");
            root.AppendChild(app);
        }
        Set(doc, app, "android", "allowBackup", AndroidNs, "false");
        string replace = app.GetAttribute("replace", ToolsNs);
        if (!replace.Contains("android:allowBackup"))
            Set(doc, app, "tools", "replace", ToolsNs, string.IsNullOrEmpty(replace) ? "android:allowBackup" : replace + ",android:allowBackup");
        doc.Save(file);
        Debug.Log("[AndroidManifestFix] allowBackup=false in " + file);
    }

    // Explicit prefixes, so the file says android:/tools: instead of generated ones.
    static void Set(XmlDocument doc, XmlElement element, string prefix, string name, string ns, string value)
    {
        var attr = element.GetAttributeNode(name, ns) ?? doc.CreateAttribute(prefix, name, ns);
        attr.Value = value;
        element.SetAttributeNode(attr);
    }
}
