using System.Text.Json.Serialization;
using Credito.Core.Analise;
using Credito.Core.Financeiro;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddSingleton(new PoliticaDeCredito());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Motor de Crédito",
    Version = "v1",
    Description = "Análise de crédito pessoal: scorecard com faixas de risco, política com cortes e capacidade de pagamento, contraproposta, "
                  + "planos Price e SAC, IOF e Custo Efetivo Total por taxa interna de retorno.",
}));

var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ArgumentOutOfRangeException e) { ctx.Response.StatusCode = 422; await ctx.Response.WriteAsJsonAsync(new { title = e.Message, status = 422 }); }
    catch (InvalidOperationException e) { ctx.Response.StatusCode = 422; await ctx.Response.WriteAsJsonAsync(new { title = e.Message, status = 422 }); }
    catch (BadHttpRequestException e) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { title = e.Message, status = 400 }); }
});
app.UseSwagger();
app.UseSwaggerUI(o => { o.RoutePrefix = "docs"; o.DocumentTitle = "Motor de Crédito"; });

var g = app.MapGroup("/api").WithTags("Crédito");

g.MapPost("/propostas/avaliar", (Proposta proposta, PoliticaDeCredito politica) => Results.Ok(politica.Avaliar(proposta, DateOnly.FromDateTime(DateTime.Today))))
    .WithSummary("Avalia uma proposta: decisão, pontuação, faixa, motivos, oferta com plano e CET, limite máximo");

g.MapPost("/simulacoes", (SimulacaoRequest req, PoliticaDeCredito politica) =>
    Results.Ok(politica.Simular(req.Valor, req.TaxaMensal, req.PrazoMeses, req.Sistema, DateOnly.FromDateTime(DateTime.Today), req.RendaMensal ?? 0m, req.OutrasParcelas ?? 0m)))
    .WithSummary("Simula um financiamento (Price ou SAC) com plano de parcelas, IOF e CET");

g.MapGet("/politica", (PoliticaDeCredito politica) => Results.Ok(new
{
    politica.Parametros,
    faixas = Enum.GetValues<FaixaDeRisco>().Select(f => new { faixa = f, taxaMensal = Scorecard.TaxaMensal(f) }),
    scorecard = Scorecard.Padrao().Select(c => new { c.Nome, faixas = c.Faixas.Select(f => new { f.Rotulo, f.Pontos }) }),
})).WithSummary("Parâmetros da política, taxas por faixa e tabela do scorecard");

app.MapGet("/saude", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record SimulacaoRequest(decimal Valor, decimal TaxaMensal, int PrazoMeses, Sistema Sistema = Sistema.Price, decimal? RendaMensal = null, decimal? OutrasParcelas = null);

public partial class Program { }
