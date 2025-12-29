using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

/// <summary>
/// PR analysis tools for the Concurrent runner.
/// Static methods with [Description] attributes for use with AIFunctionFactory.
/// </summary>
public static class PrAnalysisTools
{
    [Description("Get PR diff summary - returns statistics about changed files, risk areas, and hotspots")]
    public static string GitGetPrDiff(string pr)
        => """
           {
             "summary": {
               "filesChanged": 5,
               "insertions": 120,
               "deletions": 32,
               "riskScore": 0.68,
               "hotspots": ["src/Services/UserService.cs", "src/Controllers/AuthController.cs"]
             },
             "files": [
               {
                 "path": "src/Services/UserService.cs",
                 "added": 23,
                 "deleted": 4,
                 "risk": "high",
                 "areas": ["null-handling", "validation"],
                 "owners": ["@backend-team"]
               },
               {
                 "path": "src/Controllers/AuthController.cs",
                 "added": 12,
                 "deleted": 0,
                 "risk": "medium",
                 "areas": ["input-validation"],
                 "owners": ["@api-team"]
               },
               {
                 "path": "web/Frontend/components/SignupForm.tsx",
                 "added": 9,
                 "deleted": 2,
                 "risk": "low",
                 "areas": ["ui"],
                 "owners": ["@web"]
               }
             ],
             "constraints": {
               "parallelWorkers": 8,
               "testNaming": "*Tests.cs"
             }
           }
           """;

    [Description("Map changed files to impacted test suites with estimated runtime")]
    public static string CiGetTestMap(string files)
        => """
           {
             "suites": [
               {
                 "name": "Unit-Core",
                 "estRuntimeSec": 240,
                 "parallelizable": true,
                 "shards": 2,
                 "weight": 0.55,
                 "reasons": ["UserService changes"]
               },
               {
                 "name": "API-Contract",
                 "estRuntimeSec": 180,
                 "parallelizable": true,
                 "shards": 1,
                 "weight": 0.35,
                 "reasons": ["AuthController changes"]
               }
             ],
             "totalEstRuntimeSec": 420,
             "recommendedWorkers": 8,
             "suggestedCommand": "dotnet test -m:8"
           }
           """;

    [Description("Run a simple lint over a diff - returns lint findings and warnings")]
    public static string LintRun(string diff)
        => """
           {
             "findings": [
               {
                 "severity": "warning",
                 "rule": "CS0168",
                 "file": "src/Services/UserService.cs",
                 "line": 47,
                 "message": "Variable 'ex' is declared but never used",
                 "suggestion": "Remove unused variable or use it in logging"
               },
               {
                 "severity": "error",
                 "rule": "ASYNC001",
                 "file": "src/Controllers/AuthController.cs",
                 "line": 101,
                 "message": "Missing await for async call",
                 "suggestion": "Add await or explicitly ignore with _ = Task"
               }
             ],
             "summary": {
               "errors": 1,
               "warnings": 1
             }
           }
           """;

    [Description("Run a simple secret scan - detects potential secrets in code")]
    public static string SecretScan(string diff)
        => """
           {
             "secrets": [
               {
                 "type": "api_key",
                 "file": "web/Frontend/components/SignupForm.tsx",
                 "line": 12,
                 "entropy": 4.2
               }
             ],
             "summary": {
               "count": 1,
               "blockPR": true
             }
           }
           """;

    [Description("Check license headers in files")]
    public static string LicenseCheckHeaders(string files)
        => """
           {
             "missing": [
               { "path": "src/Controllers/AuthController.cs", "language": "csharp" }
             ],
             "summary": {
               "checked": 5,
               "missingCount": 1,
               "fixCommand": "./scripts/add-license-headers.sh"
             }
           }
           """;
}
