using System.Text.RegularExpressions;

namespace OmsLoan.Worker.Tests;

// Locks the two deploy workflows against the failure in #95: a step reported failure while the
// script it ran had succeeded. Only a native program sets $LASTEXITCODE, and every GitHub
// Actions step starts a fresh pwsh, so a script built from cmdlets alone leaves the variable
// $null. PowerShell reads $null -ne 0 as true, so `if ($LASTEXITCODE -ne 0) { throw }` fails a
// step that did nothing wrong.
//
// These read the shipped YAML as text on purpose. The bug lives in the guard as written, not in
// anything the workflow can be asked at runtime, and nothing in the repository runs these steps
// until a dispatch on cobbler1.
public class DeployWorkflowTests
{
    private static readonly string[] DeployWorkflows = ["deploy.yml", "deploy-api.yml"];

    // & "$env:GITHUB_WORKSPACE\scripts\Start-OmsLoanService.ps1"
    private static readonly Regex ScriptCall =
        new(@"&\s*""\$env:GITHUB_WORKSPACE\\scripts\\(?<script>[A-Za-z0-9.\-]+\.ps1)""");

    private static readonly Regex ExitCodeGuard = new(@"\$LASTEXITCODE\s*-ne\s*0");

    private static readonly Regex NullSafeGuard = new(@"\$null\s*-ne\s*\$LASTEXITCODE");

    private static readonly Regex StepBoundary = new(@"(?m)^\s*-\s+name:\s");

    public static TheoryData<string> Workflows => new(DeployWorkflows);

    private static string Workflow(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmsLoan.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var path = Path.Combine(directory.FullName, ".github", "workflows", name);

        Assert.True(File.Exists(path), path);

        return File.ReadAllText(path);
    }

    // The text from one script call up to whichever comes first: the next script call, or the
    // next step. A guard belongs to the call it follows, and "First ship install" runs two
    // scripts in one step.
    private static string GuardRegionAfter(string workflow, Match call)
    {
        var start = call.Index + call.Length;
        var rest = workflow[start..];

        var nextCall = ScriptCall.Match(rest);
        var nextStep = StepBoundary.Match(rest);

        var end = rest.Length;

        if (nextCall.Success)
        {
            end = Math.Min(end, nextCall.Index);
        }

        if (nextStep.Success)
        {
            end = Math.Min(end, nextStep.Index);
        }

        return rest[..end];
    }

    // The #95 regression. A script call whose guard reads $LASTEXITCODE must also accept the
    // absence of one. A call with no guard at all is left alone: the restart-after-failure
    // steps are best-effort by design and deliberately do not gate on the result.
    [Theory]
    [MemberData(nameof(Workflows))]
    public void EveryScriptGuardTreatsAnAbsentExitCodeAsSuccess(string name)
    {
        var workflow = Workflow(name);
        var calls = ScriptCall.Matches(workflow);

        Assert.NotEmpty(calls);

        var unguarded = calls
            .Where(call =>
            {
                var region = GuardRegionAfter(workflow, call);

                return ExitCodeGuard.IsMatch(region) && !NullSafeGuard.IsMatch(region);
            })
            .Select(call => call.Groups["script"].Value)
            .ToArray();

        Assert.Equal<IEnumerable<string>>([], unguarded);
    }

    // Guards on a native command are a different case and stay as they are: dotnet always sets
    // an exit code, so there is no absent code to mistake for a failure. This records that the
    // distinction is deliberate rather than a guard someone missed.
    [Theory]
    [InlineData("deploy.yml", "dotnet publish Worker")]
    [InlineData("deploy-api.yml", "dotnet publish Api")]
    public void ThePublishGuardIsLeftPlain(string name, string message)
    {
        var workflow = Workflow(name);

        Assert.Contains($@"if ($LASTEXITCODE -ne 0) {{ throw ""{message} failed", workflow, StringComparison.Ordinal);
    }

    // The second half of #95. On a first ship the service does not exist yet, so the
    // restart-after-failure step threw "is not installed" on top of the real failure and buried
    // it. The step has to look for the service before it runs the start script.
    [Theory]
    [InlineData("deploy.yml", "OmsLoanWorker")]
    [InlineData("deploy-api.yml", "OmsLoanApi")]
    public void TheRestartAfterFailureStepSkipsAServiceThatWasNeverInstalled(string name, string service)
    {
        var workflow = Workflow(name);
        var restart = workflow[workflow.IndexOf("restarting", StringComparison.Ordinal)..];
        var absenceCheck = workflow.IndexOf($"$null -eq (Get-Service -Name {service}", StringComparison.Ordinal);

        Assert.True(absenceCheck >= 0, $"{name} never checks whether {service} exists before restarting it");
        Assert.True(
            absenceCheck < workflow.IndexOf(restart, StringComparison.Ordinal),
            $"{name} checks for {service} only after announcing the restart");
    }
}
