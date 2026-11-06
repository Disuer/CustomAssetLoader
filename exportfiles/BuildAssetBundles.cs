using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copy into a Unity Editor folder. Packs wav/ogg beside the clip as .bytes, then builds Windows bundles.
/// FMOD event tracks do not need this; those sounds stay in custom_banks.
/// </summary>
public static class BuildAssetBundles
{
    [MenuItem("Build/AssetBundles/Windows64")]
    public static void Build()
    {
        SyncParallelAudio();

        string outDir = "Build/AssetBundles/Windows";
        Directory.CreateDirectory(outDir);
        BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);
        AssetDatabase.Refresh();
        Debug.Log("AssetBundles built in " + outDir);
    }

    private static void SyncParallelAudio()
    {
        string[] patterns = { "*.wav", "*.ogg" };
        int packed = 0;
        foreach (string pattern in patterns)
        {
            string[] files = Directory.GetFiles(Application.dataPath, pattern, SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string relativePath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                AssetImporter importer = AssetImporter.GetAtPath(relativePath);
                if (importer == null || string.IsNullOrEmpty(importer.assetBundleName))
                {
                    continue;
                }

                string bytesPath = relativePath + ".bytes";
                string bytesAbsolute = file + ".bytes";
                var sourceInfo = new FileInfo(file);
                var bytesInfo = new FileInfo(bytesAbsolute);
                if (!bytesInfo.Exists || sourceInfo.LastWriteTime > bytesInfo.LastWriteTime)
                {
                    File.Copy(file, bytesAbsolute, true);
                    AssetDatabase.ImportAsset(bytesPath);
                }

                AssetImporter bytesImporter = AssetImporter.GetAtPath(bytesPath);
                if (bytesImporter != null
                    && (bytesImporter.assetBundleName != importer.assetBundleName
                        || bytesImporter.assetBundleVariant != importer.assetBundleVariant))
                {
                    bytesImporter.SetAssetBundleNameAndVariant(
                        importer.assetBundleName,
                        importer.assetBundleVariant);
                }

                packed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CustomAssetLoader] Packed " + packed + " wav/ogg file(s) as .bytes.");
    }
}
