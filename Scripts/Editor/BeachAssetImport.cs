using UnityEditor;
using UnityEngine;

// Import settings for the downloaded packs, so the files can be dropped in as they ship.
sealed class BeachAssetImport : AssetPostprocessor
{
    // Kenney UI Pack ("Double" size): sliced sprites drawn at half size.
    void OnPreprocessTexture()
    {
        // The sack's and the crab's palettes are a few flat colours; the gull's is a painted atlas.
        if (assetPath.Contains("/Resources/CleanupSack/") || assetPath.Contains("/Resources/Crab/"))
        {
            var palette = (TextureImporter)assetImporter;
            palette.filterMode = FilterMode.Point;
            palette.mipmapEnabled = false;
            palette.wrapMode = TextureWrapMode.Clamp;
            palette.textureCompression = TextureImporterCompression.Uncompressed;
            return;
        }
        if (!assetPath.Contains("/UI/Resources/BeachUI/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 200;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        // left, bottom, right, top. Depth buttons keep their lower lip unstretched.
        if (file=="panel" || file=="button_pressed") importer.spriteBorder = new Vector4(24,24,24,24);
        else if (file.StartsWith("button")) importer.spriteBorder = new Vector4(24,36,24,24);
    }

    // Poly Pizza gull and crab parts and the sack: plain meshes; the game gives them their material.
    static bool Wildlife(string path) => path.Contains("/Resources/Seagull/") || path.Contains("/Resources/CleanupSack/") ||
                                         path.Contains("/Resources/Crab/");

    // Quaternius characters: clips are played by name from code, so the legacy rig is enough.
    void OnPreprocessModel()
    {
        if (Wildlife(assetPath))
        {
            var mesh = (ModelImporter)assetImporter;
            mesh.materialImportMode = ModelImporterMaterialImportMode.None;
            mesh.importNormals = ModelImporterNormals.Import;
            mesh.importAnimation = false;
            mesh.animationType = ModelImporterAnimationType.None;
            mesh.globalScale = 1;
            mesh.useFileScale = false;
            return;
        }
        if (!assetPath.Contains("/Player/Resources/BeachBody/")) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
}
