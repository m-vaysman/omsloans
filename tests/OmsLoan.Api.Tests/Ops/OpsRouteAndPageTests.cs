using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmsLoan.Api.Controllers;
using OmsLoan.Api.Ops;
using static OmsLoan.Api.Tests.Ops.OpsTestConfiguration;

namespace OmsLoan.Api.Tests.Ops;

public class OpsRouteAndPageTests
{
    private const string SpaApiPrefix = "/api";

    private static string RouteOf(string actionName) =>
        typeof(OpsController)
            .GetMethod(actionName)!
            .GetCustomAttributes<HttpGetAttribute>()
            .Single()
            .Template!;

    private static OpsController Controller() =>
        new(Configuration(), Options.Create(new OpsOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public void ThePageIsServedAtExactlyOps()
    {
        Assert.Equal("/ops", OpsRoutes.Page);
        Assert.Equal(OpsRoutes.Page, RouteOf(nameof(OpsController.Page)));
    }

    [Fact]
    public void TheStatusJsonIsServedUnderTheApiPrefix()
    {
        Assert.StartsWith($"{SpaApiPrefix}/", OpsRoutes.Status, StringComparison.Ordinal);
        Assert.Equal(OpsRoutes.Status, RouteOf(nameof(OpsController.Status)));
    }

    [Fact]
    public void ThePageNamesTheSameStatusUrlTheServerExposes()
    {
        Assert.Contains($"\"{OpsRoutes.Status}\"", OpsPage.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePagePollsEveryFiveSeconds()
    {
        Assert.Equal(5000, OpsRoutes.PollMilliseconds);
        Assert.Contains($"POLL_MS = {OpsRoutes.PollMilliseconds}", OpsPage.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageLoadsNothingFromTheInternet()
    {
        Assert.DoesNotContain("http://", OpsPage.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", OpsPage.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", OpsPage.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", OpsPage.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePageShowsBothServicesTheDatabaseAndThreeLogSections()
    {
        Assert.Contains("Worker Event Log", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("Api Event Log", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("Deploy Actions", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("\"Database\"", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("id=\"stub\"", OpsPage.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void AStubbedCardNeverRendersAsAGreenMeasuredState()
    {
        Assert.Contains("stub ? tonedPill(state, \"warn\") : pill(state)", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("chip(\"not measured\")", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("service.path, status.stub", OpsPage.Html, StringComparison.Ordinal);
        Assert.Contains("endpoint, status.stub", OpsPage.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageNeverWritesLogTextAsMarkup()
    {
        Assert.DoesNotContain("innerHTML", OpsPage.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsServedAsHtml()
    {
        var result = Controller().Page();

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        Assert.StartsWith("<!doctype html>", result.Content!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStatusResponseIsNotCachedByTheBrowser()
    {
        var controller = Controller();

        await controller.Status(CancellationToken.None);

        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }
}
