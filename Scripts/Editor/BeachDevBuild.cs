using UnityEditor;
using UnityEditor.Build.Reporting;

// A development build for trying co-op on one machine: run it twice, or once beside the editor.
// From a terminal: unity build --target StandaloneOSX --execute-method BeachDevBuild.Mac -o <path>.app
public static class BeachDevBuild
{
    [MenuItem("FindNeedle/협동 테스트용 빌드 (macOS)")]
    public static void Mac()
    {
        string path = "Builds/Dev/FindNeedle.app";
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args,"-buildOutput");
        if (at>=0 && at+1<args.Length) path = args[at+1];
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[]{"Assets/Scenes/SampleScene.unity"},
            locationPathName = path,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development
        });
        if (report.summary.result!=BuildResult.Succeeded && UnityEditorInternal.InternalEditorUtility.inBatchMode)
            EditorApplication.Exit(1);
    }
}
