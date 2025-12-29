using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

/// <summary>
/// Development workflow tools for the Sequential runner.
/// Static methods with [Description] attributes for use with AIFunctionFactory.
/// </summary>
public static class DevWorkflowTools
{
    [Description("Generate OpenAPI from story and acceptance criteria")]
    public static string OasGenerate(string story, string acceptance)
        => "{\n" +
           "  \"openapiYaml\": \"openapi: 3.1.0\\ninfo:\\n  title: Avatar Service\\n  version: 1.0.0\\npaths:\\n  /avatars:\\n    post:\\n      summary: Upload avatar up to 2MB\\n      requestBody:\\n        required: true\\n        content:\\n          multipart/form-data:\\n            schema:\\n              type: object\\n              properties:\\n                file:\\n                  type: string\\n                  format: binary\\n      responses:\\n        '201': { description: Created }\",\n" +
           "  \"hints\": {\n" +
           "    \"boundedContexts\": [\"media\"],\n" +
           "    \"nonFunctionals\": [\"size-limit:2MB\", \"auth:required\"]\n" +
           "  }\n" +
           "}";

    [Description("Create a git branch")]
    public static string RepoCreateBranch(string name)
        => "{\n" +
           "  \"branch\": \"feature/" + name + "\",\n" +
           "  \"commands\": [\n" +
           "    \"git checkout -b feature/" + name + "\",\n" +
           "    \"git push -u origin feature/" + name + "\"\n" +
           "  ]\n" +
           "}";

    [Description("Scaffold service structure")]
    public static string CreateScaffold(string branch)
        => "{\n" +
           "  \"commitSha\": \"a1b2c3d\",\n" +
           "  \"branch\": \"" + branch + "\",\n" +
           "  \"summary\": \"Committed scaffold\",\n" +
           "  \"commands\": [\n" +
           "    \"git add .\",\n" +
           "    \"git commit -m 'Scaffold service and add tests'\",\n" +
           "    \"git push\"\n" +
           "  ]\n" +
           "}";

    [Description("Generate tests from OpenAPI and acceptance criteria")]
    public static string TestsGenerate(string openapiYaml, string acceptance)
        => "{\n" +
           "  \"files\": [\n" +
           "    { \"path\": \"tests/AvatarUploadTests.cs\", \"kind\": \"contract\" },\n" +
           "    { \"path\": \"tests/AvatarSizeTests.cs\", \"kind\": \"unit\" }\n" +
           "  ],\n" +
           "  \"hints\": {\n" +
           "    \"framework\": \"xunit\",\n" +
           "    \"command\": \"dotnet test --filter Category=Contract\"\n" +
           "  }\n" +
           "}";

    [Description("Update docs and open PR")]
    public static string DocsUpdate(string branch, string summary)
        => "{\n" +
           "  \"prUrl\": \"https://example.com/pr/123\",\n" +
           "  \"branch\": \"" + branch + "\",\n" +
           "  \"title\": \"Add avatar upload with 2MB limit\",\n" +
           "  \"checklist\": [\"Docs updated\", \"Tests added\", \"API reviewed\"]\n" +
           "}";
}
