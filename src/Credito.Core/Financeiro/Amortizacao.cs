namespace Credito.Core.Financeiro;

public enum Sistema
{
    /// <summary>Parcelas fixas (Tabela Price / sistema francês).</summary>
    Price,
    /// <summary>Amortização constante, parcelas decrescentes.</summary>
    Sac,
}

public sealed record Parcela(int Numero, DateOnly Vencimento, decimal Valor, decimal Juros, decimal Amortizacao, decimal SaldoDevedor);

/// <summary>Cálculo de planos de pagamento com arredondamento a centavos e fechamento exato do saldo.</summary>
public static class Amortizacao
{
    /// <summary>PMT da Price: PV·i / (1 − (1+i)^−n). Para i = 0, PV/n.</summary>
    public static decimal ParcelaPrice(decimal principal, decimal taxaMensal, int prazo)
    {
        Validar(principal, taxaMensal, prazo);
        if (taxaMensal == 0) return decimal.Round(principal / prazo, 2, MidpointRounding.AwayFromZero);
        var i = (double)taxaMensal;
        var pmt = (double)principal * i / (1 - Math.Pow(1 + i, -prazo));
        return decimal.Round((decimal)pmt, 2, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<Parcela> Plano(Sistema sistema, decimal principal, decimal taxaMensal, int prazo, DateOnly primeiroVencimento) =>
        sistema == Sistema.Price ? Price(principal, taxaMensal, prazo, primeiroVencimento) : Sac(principal, taxaMensal, prazo, primeiroVencimento);

    public static IReadOnlyList<Parcela> Price(decimal principal, decimal taxaMensal, int prazo, DateOnly primeiroVencimento)
    {
        var pmt = ParcelaPrice(principal, taxaMensal, prazo);
        var parcelas = new List<Parcela>(prazo);
        var saldo = principal;
        for (var k = 1; k <= prazo; k++)
        {
            var juros = Arredondar(saldo * taxaMensal);
            var amortizacao = k == prazo ? saldo : pmt - juros;
            var valor = k == prazo ? amortizacao + juros : pmt; // a última parcela absorve as diferenças de centavos
            saldo = Arredondar(saldo - amortizacao);
            parcelas.Add(new Parcela(k, primeiroVencimento.AddMonths(k - 1), Arredondar(valor), juros, Arredondar(amortizacao), Math.Max(0, saldo)));
        }
        return parcelas;
    }

    public static IReadOnlyList<Parcela> Sac(decimal principal, decimal taxaMensal, int prazo, DateOnly primeiroVencimento)
    {
        Validar(principal, taxaMensal, prazo);
        var amortizacao = Arredondar(principal / prazo);
        var parcelas = new List<Parcela>(prazo);
        var saldo = principal;
        for (var k = 1; k <= prazo; k++)
        {
            var juros = Arredondar(saldo * taxaMensal);
            var amort = k == prazo ? saldo : amortizacao;
            saldo = Arredondar(saldo - amort);
            parcelas.Add(new Parcela(k, primeiroVencimento.AddMonths(k - 1), Arredondar(amort + juros), juros, amort, Math.Max(0, saldo)));
        }
        return parcelas;
    }

    public static decimal TotalPago(IEnumerable<Parcela> plano) => plano.Sum(p => p.Valor);
    public static decimal TotalJuros(IEnumerable<Parcela> plano) => plano.Sum(p => p.Juros);

    public static decimal Arredondar(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    private static void Validar(decimal principal, decimal taxa, int prazo)
    {
        if (principal <= 0) throw new ArgumentOutOfRangeException(nameof(principal), "principal deve ser positivo");
        if (taxa < 0 || taxa > 1) throw new ArgumentOutOfRangeException(nameof(taxa), "taxa mensal deve estar entre 0 e 1 (100%)");
        if (prazo is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(prazo), "prazo deve estar entre 1 e 600 meses");
    }
}
