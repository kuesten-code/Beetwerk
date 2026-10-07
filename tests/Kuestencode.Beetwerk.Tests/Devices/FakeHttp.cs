using System.Net;
using System.Text;

namespace Kuestencode.Beetwerk.Tests.Devices;

/// <summary>Simulierter HTTP-Server: Antworten je Methode und Pfad, alle Anfragen werden mitgeschnitten.</summary>
public sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
{
    public sealed record Recorded(HttpMethod Method, string Url, IReadOnlyDictionary<string, string> Headers, string? Body);

    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> responses = new();

    public List<Recorded> Requests { get; } = [];

    public FakeHttp On(HttpMethod method, string url, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        responses[$"{method} {url}"] = (status, body);
        return this;
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        if (request.Content is not null)
            headers["Content-Type"] = request.Content.Headers.ContentType?.ToString() ?? "";
        var url = request.RequestUri!.ToString();
        Requests.Add(new Recorded(request.Method, url, headers, body));

        return responses.TryGetValue($"{request.Method} {url}", out var response)
            ? new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"not found"}""") };
    }
}
