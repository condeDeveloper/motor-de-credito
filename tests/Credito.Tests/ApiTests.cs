using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Credito.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public ApiTests(WebApplicationFactory<Program> f) => _http = f.CreateClient();

    [Fact]
    public async Task AvaliaPropostaESimula()
    {
        var proposta = new { id = "api-1", valorSolicitado = 15000, prazoMeses = 18, idade = 33, rendaMensal = 8000, despesasMensais = 1500, tipoRenda = "Formal", mesesNoEmprego = 30, atrasosUltimos12Meses = 0, scoreBureau = 720, restricaoAtiva = false };
        var r = await _http.PostAsJsonAsync("/api/propostas/avaliar", proposta);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
        a.GetProperty("decisao").GetString().Should().BeOneOf("Aprovado", "AprovadoComAjuste");
        a.GetProperty("oferta").GetProperty("plano").GetArrayLength().Should().Be(18);
        a.GetProperty("oferta").GetProperty("cet").GetProperty("cetAnual").GetDecimal().Should().BeGreaterThan(0);
        a.GetProperty("detalhe").GetArrayLength().Should().Be(8);

        var s = await _http.PostAsJsonAsync("/api/simulacoes", new { valor = 10000, taxaMensal = 0.01, prazoMeses = 12, sistema = "Price" });
        var sim = await s.Content.ReadFromJsonAsync<JsonElement>(Json);
        sim.GetProperty("parcela").GetDecimal().Should().Be(888.49m);

        var invalido = await _http.PostAsJsonAsync("/api/simulacoes", new { valor = 10000, taxaMensal = 0.01, prazoMeses = 0 });
        invalido.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await _http.GetAsync("/api/politica")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
