using Credito.Core.Analise;
using Credito.Core.Financeiro;

namespace Credito.Tests;

public class AnaliseTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 16);
    private readonly PoliticaDeCredito _politica = new();

    private static Proposta Boa(decimal valor = 20_000m, int prazo = 24) => new("p1", valor, prazo, Idade: 40, RendaMensal: 12_000m, DespesasMensais: 2_000m,
        TipoRenda.Formal, MesesNoEmprego: 72, AtrasosUltimos12Meses: 0, ScoreBureau: 780, RestricaoAtiva: false, ClienteAtual: true);

    [Fact]
    public void ScorecardPontuaEClassifica()
    {
        var (pontos, detalhe) = new Scorecard().Pontuar(Boa());
        detalhe.Should().HaveCount(8);
        detalhe.Should().Contain(d => d.Caracteristica == "idade" && d.Faixa == "35 a 49" && d.Pontos == 35);
        detalhe.Should().Contain(d => d.Caracteristica == "atrasos em 12 meses" && d.Pontos == 40);
        pontos.Should().Be(35 + 35 + 25 + 30 + 40 + 45 + 20 + 10);
        Scorecard.Classificar(pontos).Should().Be(FaixaDeRisco.A);
        Scorecard.Classificar(150).Should().Be(FaixaDeRisco.B);
        Scorecard.Classificar(59).Should().Be(FaixaDeRisco.E);
        Scorecard.TaxaMensal(FaixaDeRisco.A).Should().BeLessThan(Scorecard.TaxaMensal(FaixaDeRisco.E));
    }

    [Fact]
    public void PropostaBoaEhAprovadaComOfertaECet()
    {
        var a = _politica.Avaliar(Boa(), Hoje);
        a.Decisao.Should().Be(Decisao.Aprovado);
        a.FaixaDeRisco.Should().Be(FaixaDeRisco.A);
        a.Oferta.Should().NotBeNull();
        a.Oferta!.Valor.Should().Be(20_000m);
        a.Oferta.TaxaMensal.Should().Be(0.012m);
        a.Oferta.Plano.Should().HaveCount(24);
        a.Oferta.Cet.CetAnual.Should().BeGreaterThan(a.Oferta.Cet.TaxaMensal * 12);
        a.Oferta.ComprometimentoResultante.Should().BeLessThan(0.35m);
        a.LimiteMaximo.Should().BeGreaterThan(20_000m);
        a.Motivos.Should().BeEmpty();
    }

    [Fact]
    public void CortesDurosRecusamComMotivos()
    {
        var restrito = _politica.Avaliar(Boa() with { RestricaoAtiva = true }, Hoje);
        restrito.Decisao.Should().Be(Decisao.Recusado);
        restrito.Motivos.Should().ContainSingle(m => m.Contains("restrição"));
        restrito.Oferta.Should().BeNull();

        var jovemDemais = _politica.Avaliar(Boa() with { Idade = 17, ScoreBureau = 200 }, Hoje);
        jovemDemais.Motivos.Should().HaveCount(2);

        var valorAlto = _politica.Avaliar(Boa(500_000m), Hoje);
        valorAlto.Motivos.Should().ContainSingle(m => m.Contains("valor fora"));
    }

    [Fact]
    public void FaixaEEhRecusadaFaixaDVaiParaAnalise()
    {
        var ruim = new Proposta("e", 5_000m, 12, 22, 1_800m, 900m, TipoRenda.Informal, 2, 3, 320, false);
        var a = _politica.Avaliar(ruim, Hoje);
        a.FaixaDeRisco.Should().Be(FaixaDeRisco.E);
        a.Decisao.Should().Be(Decisao.Recusado);

        var mediano = new Proposta("d", 3_000m, 12, 30, 3_000m, 600m, TipoRenda.Autonomo, 12, 1, 300, false);
        var b = _politica.Avaliar(mediano, Hoje);
        b.FaixaDeRisco.Should().Be(FaixaDeRisco.D);
        b.Decisao.Should().Be(Decisao.AnaliseManual);
        b.Oferta.Should().NotBeNull();
    }

    [Fact]
    public void ContrapropostaReduzOValorParaCaberNaRenda()
    {
        var pede = Boa(valor: 150_000m, prazo: 24) with { RendaMensal = 6_000m, DespesasMensais = 1_000m };
        var a = _politica.Avaliar(pede, Hoje);
        a.Decisao.Should().Be(Decisao.AprovadoComAjuste);
        a.Oferta!.Valor.Should().BeLessThan(150_000m);
        // a parcela ofertada cabe exatamente no comprometimento máximo
        (a.Oferta.Parcela + 1_000m).Should().BeLessThanOrEqualTo(6_000m * 0.35m + 0.5m);
        a.Motivos.Should().ContainSingle(m => m.Contains("reduzido"));
        a.LimiteMaximo.Should().Be(a.Oferta.Valor);
    }

    [Fact]
    public void RendaLivreZeroRecusa()
    {
        var a = _politica.Avaliar(Boa() with { RendaMensal = 3_000m, DespesasMensais = 2_900m }, Hoje);
        a.Decisao.Should().Be(Decisao.Recusado);
        a.Motivos.Should().ContainSingle(m => m.Contains("renda livre"));
    }

    [Fact]
    public void PrincipalParaEhInversoDaParcelaPrice()
    {
        var pv = PoliticaDeCredito.PrincipalPara(888.49m, 0.01m, 12);
        pv.Should().BeApproximately(10_000m, 0.10m);
        PoliticaDeCredito.PrincipalPara(100m, 0m, 12).Should().Be(1_200m);
    }

    [Fact]
    public void SimulacaoSacEPrice()
    {
        var price = _politica.Simular(10_000m, 0.015m, 12, Sistema.Price, Hoje, 5_000m, 500m);
        var sac = _politica.Simular(10_000m, 0.015m, 12, Sistema.Sac, Hoje, 5_000m);
        price.Plano.Select(p => p.Valor).Distinct().Should().HaveCountLessThanOrEqualTo(2); // fixas, salvo ajuste final
        sac.Plano[0].Valor.Should().BeGreaterThan(sac.Plano[^1].Valor);
        price.ComprometimentoResultante.Should().Be(decimal.Round((price.Parcela + 500m) / 5_000m, 4));
        sac.Cet.TotalJuros.Should().BeLessThan(price.Cet.TotalJuros); // SAC paga menos juros no total
    }
}
