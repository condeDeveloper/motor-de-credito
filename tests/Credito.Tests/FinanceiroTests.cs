using Credito.Core.Financeiro;

namespace Credito.Tests;

public class FinanceiroTests
{
    private static readonly DateOnly D0 = new(2026, 9, 16);

    [Fact]
    public void ParcelaPriceValorConhecido()
    {
        // 10.000 a 1% ao mês em 12 parcelas: 888,49 (valor clássico de tabela)
        Amortizacao.ParcelaPrice(10_000m, 0.01m, 12).Should().Be(888.49m);
        Amortizacao.ParcelaPrice(1_200m, 0m, 12).Should().Be(100m);
    }

    [Fact]
    public void PlanoPriceFechaOSaldoESomaOsJuros()
    {
        var plano = Amortizacao.Price(10_000m, 0.01m, 12, D0.AddMonths(1));
        plano.Should().HaveCount(12);
        plano[0].Juros.Should().Be(100m);
        plano[0].Amortizacao.Should().Be(788.49m);
        plano[^1].SaldoDevedor.Should().Be(0m);
        plano.Sum(p => p.Amortizacao).Should().Be(10_000m);
        Amortizacao.TotalPago(plano).Should().BeApproximately(888.49m * 12, 0.10m);
        plano.Select(p => p.Vencimento).Should().BeInAscendingOrder();
        plano[11].Vencimento.Should().Be(D0.AddMonths(12));
    }

    [Fact]
    public void PlanoSacTemAmortizacaoConstanteEParcelasDecrescentes()
    {
        var plano = Amortizacao.Sac(10_000m, 0.01m, 10, D0);
        plano[0].Amortizacao.Should().Be(1_000m);
        plano[0].Valor.Should().Be(1_100m);
        plano[1].Valor.Should().Be(1_090m);
        plano[^1].Valor.Should().Be(1_010m);
        plano[^1].SaldoDevedor.Should().Be(0m);
        Amortizacao.TotalJuros(plano).Should().Be(550m);
    }

    [Fact]
    public void ValidaEntradas()
    {
        var a = () => Amortizacao.ParcelaPrice(0, 0.01m, 12);
        a.Should().Throw<ArgumentOutOfRangeException>();
        var b = () => Amortizacao.Sac(100, 0.01m, 0, D0);
        b.Should().Throw<ArgumentOutOfRangeException>();
        var c = () => Amortizacao.Price(100, 1.5m, 12, D0);
        c.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CetSemEncargosEIofEhAPropriaTaxa()
    {
        var plano = Amortizacao.Price(10_000m, 0.02m, 24, D0.AddMonths(1));
        var cet = Cet.Calcular(plano, 0.02m, D0, new Encargos(CobrarIof: false));
        cet.Iof.Should().Be(0m);
        cet.ValorLiberado.Should().Be(10_000m);
        ((double)cet.CetMensal).Should().BeApproximately(0.02, 0.0005); // meses de 30 dias vs. calendário real
        ((double)cet.CetAnual).Should().BeApproximately(Math.Pow(1.02, 12) - 1, 0.01);
    }

    [Fact]
    public void IofETarifaAumentamOCet()
    {
        var plano = Amortizacao.Price(10_000m, 0.02m, 12, D0.AddMonths(1));
        var iof = Cet.CalcularIof(plano, D0);
        iof.Should().BeGreaterThan(38m).And.BeLessThan(10_000m * 0.0338m + 1);
        var sem = Cet.Calcular(plano, 0.02m, D0, new Encargos(CobrarIof: false));
        var com = Cet.Calcular(plano, 0.02m, D0, new Encargos(TarifaCadastro: 150m, SeguroMensal: 20m));
        com.ValorLiberado.Should().Be(10_000m - iof - 150m);
        com.Seguros.Should().Be(240m);
        com.CetMensal.Should().BeGreaterThan(sem.CetMensal);
        com.CetAnual.Should().BeGreaterThan(com.CetMensal * 12); // capitalização composta
    }

    [Fact]
    public void IofDiarioLimitadoA365Dias()
    {
        var curto = Amortizacao.Price(10_000m, 0.01m, 6, D0.AddMonths(1));
        var longo = Amortizacao.Price(10_000m, 0.01m, 48, D0.AddMonths(1));
        Cet.CalcularIof(longo, D0).Should().BeLessThanOrEqualTo(Amortizacao.Arredondar(10_000m * (0.0038m + 0.03m)) + 0.02m);
        Cet.CalcularIof(curto, D0).Should().BeLessThan(Cet.CalcularIof(longo, D0));
    }

    [Fact]
    public void TirRecuperaATaxaDeUmFluxoConhecido()
    {
        var fluxos = new List<(double, double)> { (0, 1000) };
        for (var k = 1; k <= 10; k++) fluxos.Add((k, -(double)Amortizacao.ParcelaPrice(1000m, 0.03m, 10)));
        Cet.Tir(fluxos).Should().BeApproximately(0.03, 0.0002);
        Cet.Tir(new List<(double, double)> { (0, 1000), (1, -500) }).Should().Be(0); // recebe mais do que paga
    }

    [Fact]
    public void EncargosMaioresQueOPrincipalSaoRejeitados()
    {
        var plano = Amortizacao.Price(500m, 0.01m, 3, D0.AddMonths(1));
        var act = () => Cet.Calcular(plano, 0.01m, D0, new Encargos(TarifaCadastro: 600m));
        act.Should().Throw<InvalidOperationException>();
    }
}
