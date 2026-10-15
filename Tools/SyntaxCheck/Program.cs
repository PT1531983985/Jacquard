using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
var files = Directory.GetFiles(Path.Combine(root, "Assets"), "*.cs", SearchOption.AllDirectories);
string[][] configurations = [[], ["UNITY_EDITOR", "UNITY_STANDALONE"], ["UNITY_STANDALONE"], ["UNITY_WEBGL"], ["UNITY_ANDROID"], ["UNITY_IOS"]];
var failures = 0;
foreach (var symbols in configurations)
    foreach (var file in files)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file, System.Text.Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols), file);
        foreach (var diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            Console.Error.WriteLine($"[{string.Join(',', symbols)}] {diagnostic}");
            failures++;
        }
    }
Console.WriteLine($"Parsed {files.Length} Unity C# files in {configurations.Length} symbol configurations: {failures} syntax errors.");
return failures == 0 ? 0 : 1;
