using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PermitDesk.Api.Integrations;

namespace PermitDesk.Tests;

public class ZoningClientTests
{
    private class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ZoningClient ClientFor(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://zoning.test/") }, NullLogger<ZoningClient>.Instance);

    [Fact]
    public async Task SubmitCheck_posts_the_parcel_and_returns_the_handle()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Accepted, "{\"handle\":\"zc-123\"}"));

        var handle = await ClientFor(handler).SubmitCheckAsync("EB-1120-03", "addition", "Rear addition", CancellationToken.None);

        Assert.Equal("zc-123", handle);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/checks", request.Path);
        Assert.Contains("\"parcelNumber\":\"EB-1120-03\"", request.Body);
        Assert.Contains("\"workType\":\"addition\"", request.Body);
    }

    [Fact]
    public async Task GetCheck_reads_a_completed_result()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"handle\":\"zc-123\",\"status\":\"complete\",\"result\":\"fail\",\"reason\":\"Not permitted in OS\"}"));

        var status = await ClientFor(handler).GetCheckAsync("zc-123", CancellationToken.None);

        Assert.Equal("complete", status.Status);
        Assert.Equal("fail", status.Result);
        Assert.Equal("Not permitted in OS", status.Reason);
        Assert.Equal("/checks/zc-123", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task GetCheck_reads_a_pending_check()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            "{\"handle\":\"zc-9\",\"status\":\"pending\",\"result\":null,\"reason\":null}"));

        var status = await ClientFor(handler).GetCheckAsync("zc-9", CancellationToken.None);

        Assert.Equal("pending", status.Status);
        Assert.Null(status.Result);
    }
}
