using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class PrAnalysisPlugin
{
    // Diff and PR analysis helpers
    [KernelFunction, Description("Get PR diff summary")]
    public string Git_GetPRDiff([Description("PR id, URL, or text context")] string pr)
        => "{\n" +
           "  \"summary\": {\n" +
           "    \"filesChanged\": 5,\n" +
           "    \"insertions\": 120,\n" +
           "    \"deletions\": 32,\n" +
           "    \"riskScore\": 0.68,\n" +
           "    \"hotspots\": [\"src/Services/UserService.cs\", \"src/Controllers/AuthController.cs\"]\n" +
           "  },\n" +
           "  \"files\": [\n" +
           "    {\n" +
           "      \"path\": \"src/Services/UserService.cs\",\n" +
           "      \"added\": 23,\n" +
           "      \"deleted\": 4,\n" +
           "      \"risk\": \"high\",\n" +
           "      \"areas\": [\"null-handling\", \"validation\"],\n" +
           "      \"owners\": [\"@backend-team\"]\n" +
           "    },\n" +
           "    {\n" +
           "      \"path\": \"src/Controllers/AuthController.cs\",\n" +
           "      \"added\": 12,\n" +
           "      \"deleted\": 0,\n" +
           "      \"risk\": \"medium\",\n" +
           "      \"areas\": [\"input-validation\"],\n" +
           "      \"owners\": [\"@api-team\"]\n" +
           "    },\n" +
           "    {\n" +
           "      \"path\": \"web/Frontend/components/SignupForm.tsx\",\n" +
           "      \"added\": 9,\n" +
           "      \"deleted\": 2,\n" +
           "      \"risk\": \"low\",\n" +
           "      \"areas\": [\"ui\"],\n" +
           "      \"owners\": [\"@web\"]\n" +
           "    }\n" +
           "  ],\n" +
           "  \"constraints\": {\n" +
           "    \"parallelWorkers\": 8,\n" +
           "    \"testNaming\": \"*Tests.cs\"\n" +
           "  }\n" +
           "}";

    [KernelFunction, Description("Map changed files to impacted test suites")]
    public string CI_GetTestMap([Description("files JSON from Git_GetPRDiff")] string files)
        => "{\n" +
           "  \"suites\": [\n" +
           "    {\n" +
           "      \"name\": \"Unit-Core\",\n" +
           "      \"estRuntimeSec\": 240,\n" +
           "      \"parallelizable\": true,\n" +
           "      \"shards\": 2,\n" +
           "      \"weight\": 0.55,\n" +
           "      \"reasons\": [\"UserService changes\"]\n" +
           "    },\n" +
           "    {\n" +
           "      \"name\": \"API-Contract\",\n" +
           "      \"estRuntimeSec\": 180,\n" +
           "      \"parallelizable\": true,\n" +
           "      \"shards\": 1,\n" +
           "      \"weight\": 0.35,\n" +
           "      \"reasons\": [\"AuthController changes\"]\n" +
           "    }\n" +
           "  ],\n" +
           "  \"totalEstRuntimeSec\": 420,\n" +
           "  \"recommendedWorkers\": 8,\n" +
           "  \"suggestedCommand\": \"dotnet test -m:8\"\n" +
           "}";

    [KernelFunction, Description("Run a simple lint over a diff")]
    public string Lint_Run([Description("diff JSON")] string diff)
        => "{\n" +
           "  \"findings\": [\n" +
           "    {\n" +
           "      \"severity\": \"warning\",\n" +
           "      \"rule\": \"CS0168\",\n" +
           "      \"file\": \"src/Services/UserService.cs\",\n" +
           "      \"line\": 47,\n" +
           "      \"message\": \"Variable 'ex' is declared but never used\",\n" +
           "      \"suggestion\": \"Remove unused variable or use it in logging\"\n" +
           "    },\n" +
           "    {\n" +
           "      \"severity\": \"error\",\n" +
           "      \"rule\": \"ASYNC001\",\n" +
           "      \"file\": \"src/Controllers/AuthController.cs\",\n" +
           "      \"line\": 101,\n" +
           "      \"message\": \"Missing await for async call\",\n" +
           "      \"suggestion\": \"Add await or explicitly ignore with _ = Task\"\n" +
           "    }\n" +
           "  ],\n" +
           "  \"summary\": {\n" +
           "    \"errors\": 1,\n" +
           "    \"warnings\": 1\n" +
           "  }\n" +
           "}";

    [KernelFunction, Description("Run a simple secret scan")]
    public string Secret_Scan([Description("diff JSON")] string diff)
        => "{\n" +
           "  \"secrets\": [\n" +
           "    {\n" +
           "      \"type\": \"api_key\",\n" +
           "      \"file\": \"web/Frontend/components/SignupForm.tsx\",\n" +
           "      \"line\": 12,\n" +
           "      \"entropy\": 4.2\n" +
           "    }\n" +
           "  ],\n" +
           "  \"summary\": {\n" +
           "    \"count\": 1,\n" +
           "    \"blockPR\": true\n" +
           "  }\n" +
           "}";

    [KernelFunction, Description("Check license headers")]
    public string License_CheckHeaders([Description("files JSON")] string files)
        => "{\n" +
           "  \"missing\": [\n" +
           "    { \"path\": \"src/Controllers/AuthController.cs\", \"language\": \"csharp\" }\n" +
           "  ],\n" +
           "  \"summary\": {\n" +
           "    \"checked\": 5,\n" +
           "    \"missingCount\": 1,\n" +
           "    \"fixCommand\": \"./scripts/add-license-headers.sh\"\n" +
           "  }\n" +
           "}";
}


