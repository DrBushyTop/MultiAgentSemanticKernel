using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MultiAgentSemanticKernel.Plugins;

public sealed class DevWorkflowPlugin
{
    // Story → OAS → Code/Test/Docs workflow helpers
    [KernelFunction, Description("Generate OpenAPI from story and AC")]
    public string Oas_Generate([Description("story")] string story, [Description("acceptance JSON")] string acceptance)
        => "{\n" +
           "  \"openapiYaml\": \"openapi: 3.1.0\\ninfo:\\n  title: Avatar Service\\n  version: 1.0.0\\npaths:\\n  /avatars:\\n    post:\\n      summary: Upload avatar up to 2MB\\n      requestBody:\\n        required: true\\n        content:\\n          multipart/form-data:\\n            schema:\\n              type: object\\n              properties:\\n                file:\\n                  type: string\\n                  format: binary\\n      responses:\\n        '201': { description: Created }\",\n" +
           "  \"hints\": {\n" +
           "    \"boundedContexts\": [\"media\"],\n" +
           "    \"nonFunctionals\": [\"size-limit:2MB\", \"auth:required\"]\n" +
           "  }\n" +
           "}";

    [KernelFunction, Description("Create a git branch")]
    public string Repo_CreateBranch([Description("name")] string name)
        => "{\n" +
           "  \"branch\": \"feature/\" + name,\n" +
           "  \"commands\": [\n" +
           "    \"git checkout -b feature/" + name + "\",\n" +
           "    \"git push -u origin feature/" + name + "\"\n" +
           "  ]\n" +
           "}";

    [KernelFunction, Description("Commit changes")]
    public string Repo_Commit([Description("branch")] string branch, [Description("changes JSON")] string changes)
        => "{\n" +
           "  \"commitSha\": \"a1b2c3d\",\n" +
           "  \"branch\": \"" + branch + "\",\n" +
           "  \"summary\": \"Committed scaffold and tests\",\n" +
           "  \"commands\": [\n" +
           "    \"git add .\",\n" +
           "    \"git commit -m 'Scaffold service and add tests'\",\n" +
           "    \"git push\"\n" +
           "  ]\n" +
           "}";

    [KernelFunction, Description("Generate tests from OAS and AC")]
    public string Tests_Generate([Description("openapiYaml")] string openapiYaml, [Description("acceptance JSON")] string acceptance)
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

    [KernelFunction, Description("Update docs and open PR")]
    public string Docs_Update([Description("branch")] string branch, [Description("summary")] string summary)
        => "{\n" +
           "  \"prUrl\": \"https://example.com/pr/123\",\n" +
           "  \"branch\": \"" + branch + "\",\n" +
           "  \"title\": \"Add avatar upload with 2MB limit\",\n" +
           "  \"checklist\": [\"Docs updated\", \"Tests added\", \"API reviewed\"]\n" +
           "}";
}


