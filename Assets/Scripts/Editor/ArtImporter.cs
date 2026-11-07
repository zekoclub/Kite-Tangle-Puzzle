using UnityEditor;
using UnityEngine;

// Import rules for the art in Assets/Resources/Art, so dropping in a new PNG needs no manual setup.
// Every sprite uses 100 pixels per unit; the game code scales sprites to the world size it wants.
public class ArtImporter : AssetPostprocessor
{
    const string ArtRoot = "Assets/Resources/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(ArtRoot)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; // needed for 9-slicing
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        settings.spriteBorder = Vector4.zero;

        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        if (name.StartsWith("kite_"))
        {
            // Pivot on the point where the string meets the kite (design y=22 in a -23..33 viewBox).
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 11f / 56f);
        }
        else if (name.StartsWith("button_"))
            settings.spriteBorder = new Vector4(80, 80, 80, 80);   // 20 design px at 4x
        else if (name.StartsWith("rooftop_"))
            settings.spriteBorder = new Vector4(48, 48, 48, 48);   // 12 design px at 4x
        else if (name.StartsWith("slot_"))
            settings.spriteBorder = new Vector4(56, 56, 56, 56);
        else if (name.StartsWith("sky_") || name == "win_yellow" || name == "roof_band")
        {
            // Smooth gradients: no compression (it bands), but half size is plenty for a gradient.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
        }

        importer.SetTextureSettings(settings);
    }
}
