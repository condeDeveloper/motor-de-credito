namespace Credito.Core.Financeiro;

/// <summary>Encargos que entram no Custo Efetivo Total além dos juros.</summary>
public sealed record Encargos(decimal TarifaCadastro = 0m, decimal SeguroMensal = 0m, bool CobrarIof = true)
{
    /// <summary>IOF fixo sobre o principal (0,38%).</summary>
    public const decimal IofFixo = 0.0038m;
    /// <summary>IOF diário sobre cada amortização (0,0082% ao dia), limitado a 365 dias (3%).</summary>
    public const decimal IofDiario = 0.000082m;
    public const int IofDiasMaximos = 365;
}

public sealed record ResultadoCet(
    decimal ValorLiberado,
    decimal Iof,
    decimal Tarifas,
    decimal Seguros,
    decimal TotalPago,
    decimal TotalJuros,
    decimal TaxaMensal,
    decimal CetMensal,
    decimal CetAnual);

/// <summary>
/// Custo Efetivo Total: a taxa interna de retorno dos fluxos reais do cliente (recebe o líquido, paga as parcelas
/// mais seguros), conforme a Resolução 3.517 do CMN, anualizada por (1 + i)^12 − 1.
/// </summary>
public static class Cet
{
    public static decimal CalcularIof(IReadOnlyList<Parcela> plano, DateOnly liberacao)
    {
        var principal = plano.Sum(p => p.Amortizacao);
        var fixo = principal * Encargos.IofFixo;
        var diario = plano.Sum(p => p.Amortizacao * Encargos.IofDiario * Math.Min(Encargos.IofDiasMaximos, Math.Max(0, p.Vencimento.DayNumber - liberacao.DayNumber)));
        return Amortizacao.Arredondar(fixo + diario);
    }

    public static ResultadoCet Calcular(IReadOnlyList<Parcela> plano, decimal taxaMensal, DateOnly liberacao, Encargos encargos)
    {
        var principal = plano.Sum(p => p.Amortizacao);
        var iof = encargos.CobrarIof ? CalcularIof(plano, liberacao) : 0m;
        var liberado = Amortizacao.Arredondar(principal - iof - encargos.TarifaCadastro);
        if (liberado <= 0) throw new InvalidOperationException("encargos maiores que o principal");

        // fluxos: t0 recebe o líquido; a cada mês paga parcela + seguro
        var fluxos = new List<(double T, double Valor)> { (0, (double)liberado) };
        foreach (var p in plano)
        {
            var meses = MesesEntre(liberacao, p.Vencimento);
            fluxos.Add((meses, -(double)(p.Valor + encargos.SeguroMensal)));
        }
        var cetMensal = Tir(fluxos);
        var cetAnual = Math.Pow(1 + cetMensal, 12) - 1;
        return new ResultadoCet(liberado, iof, encargos.TarifaCadastro, Amortizacao.Arredondar(encargos.SeguroMensal * plano.Count),
            Amortizacao.Arredondar(Amortizacao.TotalPago(plano) + encargos.SeguroMensal * plano.Count), Amortizacao.TotalJuros(plano), taxaMensal,
            decimal.Round((decimal)cetMensal, 6), decimal.Round((decimal)cetAnual, 6));
    }

    /// <summary>Meses fracionários entre duas datas, com dias corridos / 30.</summary>
    public static double MesesEntre(DateOnly de, DateOnly ate) => (ate.DayNumber - de.DayNumber) / 30.0;

    /// <summary>Taxa interna de retorno por bisseção sobre VPL(i) = Σ valor·(1+i)^−t, robusta para fluxos de empréstimo.</summary>
    public static double Tir(IReadOnlyList<(double T, double Valor)> fluxos, double precisao = 1e-10)
    {
        double Vpl(double i) => fluxos.Sum(f => f.Valor / Math.Pow(1 + i, f.T));
        // num empréstimo o VPL é negativo em i = 0 (paga-se mais do que se recebe) e cresce com i até a raiz
        double baixo = 0.0, alto = 1.0; // 0% a 100% ao mês
        if (Vpl(baixo) >= 0) return 0; // cliente paga menos do que recebe: CET zero
        for (var k = 0; k < 200 && alto - baixo > precisao; k++)
        {
            var meio = (baixo + alto) / 2;
            if (Vpl(meio) < 0) baixo = meio; else alto = meio;
        }
        return (baixo + alto) / 2;
    }
}
