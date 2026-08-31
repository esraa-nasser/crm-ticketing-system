using System.Net;
using System.Text;
using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Client.Tests.Services;

/// <summary>
/// The only tests in this story that exercise the real <see cref="TicketsApiClient"/>.
/// The seam is a stubbed <see cref="HttpMessageHandler"/>, so the transport is faked
/// and the class under test is the production one. No socket, no API, no database.
/// </summary>
public sealed class TicketsApiClientTests
{
    // Captured verbatim from the live API on 30 August 2026. A hand-written fixture
    // would only prove the client agrees with the plan's guess at the wire format.
    private const string Conflict409 = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
          "title": "The request conflicts with the current state of the ticket.",
          "status": 409,
          "from": "Closed",
          "to": "Open",
          "traceId": "00-df25504426db099d2c88553dfaae58f0-ea389e60daf998d3-00"
        }
        """;

    private const string BadFilter400 = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "One or more validation errors occurred.",
          "status": 400,
          "errors": {
            "status": [
              "'Frozen' is not a recognised value."
            ]
          },
          "traceId": "00-60b99395e45fa860cabc135929420e97-cbf38d445ea91834-00"
        }
        """;

    private const string List200 = """
        {
          "items": [
            {
              "id": "01a052c3-534d-7282-90db-c9c738ac4ad0",
              "title": "Second ticket",
              "status": "Open",
              "priority": "High",
              "category": "Billing",
              "requesterId": "11111111-1111-1111-1111-111111111111",
              "assigneeId": null,
              "createdAt": "2026-08-30T13:02:07.693297+00:00",
              "updatedAt": "2026-08-30T13:02:07.981166+00:00"
            },
            {
              "id": "01a04e63-7463-7f30-8477-3a033ba4261f",
              "title": "First real ticket",
              "status": "Closed",
              "priority": "High",
              "category": "Billing",
              "requesterId": "11111111-1111-1111-1111-111111111111",
              "assigneeId": null,
              "createdAt": "2026-08-29T16:38:55.84318+00:00",
              "updatedAt": "2026-08-29T16:42:38.727353+00:00"
            }
          ],
          "page": 1,
          "pageSize": 2,
          "totalCount": 2
        }
        """;

    private sealed class StubHandler(HttpStatusCode statusCode, string body, string mediaType = "application/problem+json")
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            });
        }
    }

    private static (TicketsApiClient Client, StubHandler Handler) CreateClient(
        HttpStatusCode statusCode,
        string body,
        string mediaType = "application/problem+json")
    {
        var handler = new StubHandler(statusCode, body, mediaType);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        return (new TicketsApiClient(httpClient), handler);
    }

    [Fact]
    public async Task GetTicketsAsync_ThrowsWithTheProblemTitle_WhenTheApiRejectsTheRequest()
    {
        var (client, _) = CreateClient(HttpStatusCode.Conflict, Conflict409);

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.GetTicketsAsync(null, null, 1, CancellationToken.None));

        Assert.Equal("The request conflicts with the current state of the ticket.", ex.Message);
        Assert.Equal(409, ex.StatusCode);
        Assert.DoesNotContain("traceId", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTicketsAsync_DeserialisesACapturedPagedResponse()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, List200, "application/json");

        var result = await client.GetTicketsAsync(null, null, 1, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalCount);

        // Matched by id, never by position: row order is unverified (issue #29).
        var second = result.Items.Single(t => t.Id == Guid.Parse("01a052c3-534d-7282-90db-c9c738ac4ad0"));
        Assert.Equal("Second ticket", second.Title);
        Assert.Equal("Open", second.Status);
        Assert.Equal("Billing", second.Category);
        Assert.Null(second.AssigneeId);
    }

    [Fact]
    public async Task GetTicketsAsync_OmitsAbsentFilters()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, List200, "application/json");

        await client.GetTicketsAsync(null, null, 1, CancellationToken.None);

        var uri = handler.LastRequestUri!.ToString();
        Assert.DoesNotContain("status=", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("priority=", uri, StringComparison.Ordinal);
        Assert.Contains("page=1", uri, StringComparison.Ordinal);
        Assert.Contains($"pageSize={TicketsApiClient.PageSize}", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTicketsAsync_SendsFiltersThatArePresent()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, List200, "application/json");

        await client.GetTicketsAsync("Open", "High", 3, CancellationToken.None);

        var uri = handler.LastRequestUri!.ToString();
        Assert.Contains("status=Open", uri, StringComparison.Ordinal);
        Assert.Contains("priority=High", uri, StringComparison.Ordinal);
        Assert.Contains("page=3", uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTicketsAsync_PrefersTheValidationMessageOverTheGenericTitle()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, BadFilter400);

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.GetTicketsAsync("Frozen", null, 1, CancellationToken.None));

        Assert.Equal("'Frozen' is not a recognised value.", ex.Message);
        Assert.NotEqual("One or more validation errors occurred.", ex.Message);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetTicketsAsync_SurvivesANonJsonErrorBody()
    {
        var (client, _) = CreateClient(
            HttpStatusCode.BadGateway,
            "<html><body>502 Bad Gateway</body></html>",
            "text/html");

        var ex = await Assert.ThrowsAsync<ApiRequestException>(
            () => client.GetTicketsAsync(null, null, 1, CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public async Task GetMetadataAsync_DeserialisesTheMetadataResponse()
    {
        const string body = """
            {
              "statuses": ["New", "Open"],
              "priorities": ["Low", "Normal"],
              "transitions": { "New": ["Open"], "Open": [] }
            }
            """;

        var (client, handler) = CreateClient(HttpStatusCode.OK, body, "application/json");

        var metadata = await client.GetMetadataAsync(CancellationToken.None);

        Assert.Equal(2, metadata.Statuses.Count);
        Assert.Equal(2, metadata.Priorities.Count);
        Assert.Empty(metadata.Transitions["Open"]);
        Assert.Contains("api/tickets/metadata", handler.LastRequestUri!.ToString(), StringComparison.Ordinal);
    }
}
