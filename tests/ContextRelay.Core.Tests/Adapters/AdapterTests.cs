using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Router;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class AdapterTests
{
    [Fact]
    public async Task MailSearchAdapter_MapsMessagesToContextItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateClient("""
            {
              "value": [
                {
                  "id": "m1",
                  "subject": "Budget review",
                  "bodyPreview": "Please review the updated numbers.",
                  "receivedDateTime": "2026-04-19T12:00:00Z",
                  "webLink": "https://outlook.office.com/mail/read/m1",
                  "from": {
                    "emailAddress": {
                      "name": "Alice",
                      "address": "alice@contoso.com"
                    }
                  }
                }
              ]
            }
            """);

        var adapter = new MailSearchAdapter(new GraphHttpClient(httpClient));
        var results = await adapter.SearchAsync("token", "budget", 10, cancellationToken);

        Assert.Single(results);
        Assert.Equal(ContextSource.Mail, results[0].Source);
        Assert.Equal("Budget review", results[0].Title);
        Assert.Equal("Alice", results[0].Metadata["senderName"]);
    }

    [Fact]
    public async Task TeamsSearchAdapter_MapsSearchHitsToContextItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateClient("""
            {
              "value": [
                {
                  "hitsContainers": [
                    {
                      "hits": [
                        {
                          "summary": "Decision captured in the sprint review.",
                          "resource": {
                            "webUrl": "https://teams.microsoft.com/l/message/1",
                            "createdDateTime": "2026-04-19T12:10:00Z",
                            "from": { "emailAddress": { "name": "Bob" } },
                            "channelIdentity": { "channelDisplayName": "Sprint Review" }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """);

        var adapter = new TeamsSearchAdapter(new GraphHttpClient(httpClient));
        var results = await adapter.SearchAsync("token", "decision", 10, cancellationToken);

        Assert.Single(results);
        Assert.Equal("Bob — Sprint Review", results[0].Title);
        Assert.Equal("Decision captured in the sprint review.", results[0].Snippet);
    }

    [Fact]
    public async Task RetrievalSearchAdapter_FiltersSharePointAndOneDriveByUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string responseJson = """
            {
              "value": [
                {
                  "hitsContainers": [
                    {
                      "hits": [
                        {
                          "summary": "<c0>Architecture</c0> diagram",
                          "resource": {
                            "id": "s1",
                            "name": "Architecture.pptx",
                            "webUrl": "https://contoso.sharepoint.com/sites/eng/Shared%20Documents/Architecture.pptx",
                            "lastModifiedDateTime": "2026-04-19T10:00:00Z",
                            "parentReference": { "siteId": "site-1", "path": "/drive/root:/Shared Documents" }
                          }
                        },
                        {
                          "summary": "Personal planning notes",
                          "resource": {
                            "id": "o1",
                            "name": "plan.docx",
                            "webUrl": "https://contoso-my.sharepoint.com/personal/user/Documents/plan.docx",
                            "lastModifiedDateTime": "2026-04-19T10:05:00Z",
                            "parentReference": { "driveId": "drive-1", "path": "/drive/root:/Documents" }
                          }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        using var sharePointClient = CreateClient(responseJson);
        using var oneDriveClient = CreateClient(responseJson);

        var sharePointResults = await new RetrievalSearchAdapter(RetrievalDataSource.SharePoint, new GraphHttpClient(sharePointClient))
            .SearchAsync("token", "architecture", 10, cancellationToken);
        var oneDriveResults = await new RetrievalSearchAdapter(RetrievalDataSource.OneDriveBusiness, new GraphHttpClient(oneDriveClient))
            .SearchAsync("token", "plan", 10, cancellationToken);

        Assert.Single(sharePointResults);
        Assert.Equal(ContextSource.SharePoint, sharePointResults[0].Source);
        Assert.Single(oneDriveResults);
        Assert.Equal(ContextSource.OneDrive, oneDriveResults[0].Source);
    }

    [Fact]
    public async Task RetrievalSearchAdapter_MapsExternalItemsToConnectors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = CreateClient("""
            {
              "retrievalHits": [
                {
                  "webUrl": "https://contoso.service/item/42",
                  "resourceType": "externalItem",
                  "extracts": [
                    { "text": "Connector result one." },
                    { "text": "Connector result two." }
                  ],
                  "resourceMetadata": {
                    "title": "Contoso ticket"
                  }
                }
              ]
            }
            """);

        var adapter = new RetrievalSearchAdapter(RetrievalDataSource.ExternalItem, new GraphHttpClient(httpClient));
        var results = await adapter.SearchAsync("token", "ticket", 10, cancellationToken);

        Assert.Single(results);
        Assert.Equal(ContextSource.Connectors, results[0].Source);
        Assert.Equal("Contoso ticket", results[0].Title);
        Assert.Contains("Connector result one.", results[0].Snippet);
    }

    [Fact]
    public async Task CopilotChatAdapter_ReturnsAssistantReply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new QueueHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, """{ "id": "conversation-1" }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize the pinned docs." },
                    { "text": "Here is the assistant reply." }
                  ]
                }
                """)));

        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));
        var reply = await adapter.AskAsync("token", "Summarize the pinned docs.", cancellationToken);

        Assert.Equal("Here is the assistant reply.", reply);
    }

    [Fact]
    public async Task CopilotChatAdapter_SendMessage_IncludesExplicitContextPayloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(CreateResponse(HttpStatusCode.OK, """
            {
              "messages": [
                { "text": "Use this context." },
                { "text": "Context-aware reply." }
              ]
            }
            """)));
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync(
            "token",
            "conversation-1",
            "Use this context.",
            new CopilotChatSendOptions
            {
                AdditionalContext = new[]
                {
                    new CopilotContextMessage
                    {
                        Description = "Pinned doc",
                        Text = "Pinned content"
                    }
                },
                ContextualResources = new CopilotContextualResources
                {
                    Files = new[]
                    {
                        new CopilotContextualFileResource { Uri = "https://contoso.sharepoint.com/sites/docs/file.docx" }
                    }
                }
            },
            cancellationToken);

        Assert.Equal("Context-aware reply.", reply);
        var body = RecordingHttpMessageHandler.LastRequestBody;
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("Use this context.", root.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal("Pinned content", root.GetProperty("additionalContext")[0].GetProperty("text").GetString());
        Assert.Equal("Pinned doc", root.GetProperty("additionalContext")[0].GetProperty("description").GetString());
        Assert.Equal("https://contoso.sharepoint.com/sites/docs/file.docx", root.GetProperty("contextualResources").GetProperty("files")[0].GetProperty("uri").GetString());
    }

    [Fact]
    public async Task GraphHttpClient_RetriesOnThrottle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new QueueHttpMessageHandler(
            CreateResponse((HttpStatusCode)429, """{ "error": { "code": "TooManyRequests" } }""", retryAfterSeconds: 0),
            CreateResponse(HttpStatusCode.OK, """{ "value": [] }""")));

        var logger = new RecordingGraphLogger();
        var client = new GraphHttpClient(httpClient, logger);
        using var response = await client.SendWithRetryAsync("https://graph.microsoft.com/v1.0/me", "token", HttpMethod.Get, cancellationToken: cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(logger.Messages, message => message.Contains("throttled", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient CreateClient(string responseBody)
    {
        return new HttpClient(new QueueHttpMessageHandler(CreateResponse(HttpStatusCode.OK, responseBody)));
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        if (retryAfterSeconds.HasValue)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds.Value));
        }

        return response;
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] responses;
        private int index;

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            this.responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return Task.FromResult(current);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        public static string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class RecordingGraphLogger : IGraphLogger
    {
        public System.Collections.Generic.List<string> Messages { get; } = new();

        public void Log(string message)
        {
            Messages.Add(message);
        }
    }
}
