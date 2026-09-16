namespace Credito.Core.Analise;

public sealed record PontoDoScore(string Caracteristica, string Faixa, int Pontos);

public enum FaixaDeRisco { A, B, C, D, E }

/// <summary>
/// Scorecard aditivo: cada característica cai numa faixa que vale pontos; a soma define a faixa de risco.
/// Os pesos são declarativos, para serem calibrados sem mexer no código.
/// </summary>
public sealed class Scorecard
{
    public sealed record Faixa(string Rotulo, Func<Proposta, bool> Condicao, int Pontos);
    public sealed record Caracteristica(string Nome, IReadOnlyList<Faixa> Faixas);

    private readonly IReadOnlyList<Caracteristica> _caracteristicas;

    public Scorecard(IReadOnlyList<Caracteristica>? caracteristicas = null) => _caracteristicas = caracteristicas ?? Padrao();

    public (int Total, IReadOnlyList<PontoDoScore> Detalhe) Pontuar(Proposta p)
    {
        var detalhe = new List<PontoDoScore>();
        foreach (var c in _caracteristicas)
        {
            var faixa = c.Faixas.FirstOrDefault(f => f.Condicao(p)) ?? new Faixa("sem faixa", _ => true, 0);
            detalhe.Add(new PontoDoScore(c.Nome, faixa.Rotulo, faixa.Pontos));
        }
        return (detalhe.Sum(d => d.Pontos), detalhe);
    }

    public static FaixaDeRisco Classificar(int pontos) => pontos switch
    {
        >= 180 => FaixaDeRisco.A,
        >= 140 => FaixaDeRisco.B,
        >= 100 => FaixaDeRisco.C,
        >= 60 => FaixaDeRisco.D,
        _ => FaixaDeRisco.E,
    };

    /// <summary>Taxa mensal por faixa (precificação baseada em risco).</summary>
    public static decimal TaxaMensal(FaixaDeRisco f) => f switch
    {
        FaixaDeRisco.A => 0.012m,
        FaixaDeRisco.B => 0.018m,
        FaixaDeRisco.C => 0.025m,
        FaixaDeRisco.D => 0.035m,
        _ => 0.05m,
    };

    public static IReadOnlyList<Caracteristica> Padrao() => new[]
    {
        new Caracteristica("idade", new[]
        {
            new Faixa("18 a 24", p => p.Idade < 25, 10),
            new Faixa("25 a 34", p => p.Idade < 35, 25),
            new Faixa("35 a 49", p => p.Idade < 50, 35),
            new Faixa("50 a 64", p => p.Idade < 65, 30),
            new Faixa("65 ou mais", _ => true, 15),
        }),
        new Caracteristica("renda", new[]
        {
            new Faixa("até 2 mil", p => p.RendaMensal < 2000, 5),
            new Faixa("2 a 5 mil", p => p.RendaMensal < 5000, 15),
            new Faixa("5 a 10 mil", p => p.RendaMensal < 10000, 25),
            new Faixa("10 a 20 mil", p => p.RendaMensal < 20000, 35),
            new Faixa("acima de 20 mil", _ => true, 40),
        }),
        new Caracteristica("tipo de renda", new[]
        {
            new Faixa("servidor público", p => p.TipoRenda == TipoRenda.ServidorPublico, 30),
            new Faixa("formal", p => p.TipoRenda == TipoRenda.Formal, 25),
            new Faixa("aposentado", p => p.TipoRenda == TipoRenda.Aposentado, 20),
            new Faixa("autônomo", p => p.TipoRenda == TipoRenda.Autonomo, 10),
            new Faixa("informal", _ => true, 0),
        }),
        new Caracteristica("tempo no emprego", new[]
        {
            new Faixa("menos de 6 meses", p => p.MesesNoEmprego < 6, 0),
            new Faixa("6 a 23 meses", p => p.MesesNoEmprego < 24, 10),
            new Faixa("2 a 4 anos", p => p.MesesNoEmprego < 60, 20),
            new Faixa("5 anos ou mais", _ => true, 30),
        }),
        new Caracteristica("atrasos em 12 meses", new[]
        {
            new Faixa("nenhum", p => p.AtrasosUltimos12Meses == 0, 40),
            new Faixa("1 ou 2", p => p.AtrasosUltimos12Meses <= 2, 15),
            new Faixa("3 ou mais", _ => true, -20),
        }),
        new Caracteristica("score do bureau", new[]
        {
            new Faixa("até 300", p => p.ScoreBureau <= 300, 0),
            new Faixa("301 a 500", p => p.ScoreBureau <= 500, 15),
            new Faixa("501 a 700", p => p.ScoreBureau <= 700, 30),
            new Faixa("701 a 850", p => p.ScoreBureau <= 850, 45),
            new Faixa("acima de 850", _ => true, 55),
        }),
        new Caracteristica("comprometimento atual", new[]
        {
            new Faixa("até 20%", p => Comprometimento(p) <= 0.20m, 20),
            new Faixa("20% a 35%", p => Comprometimento(p) <= 0.35m, 10),
            new Faixa("35% a 50%", p => Comprometimento(p) <= 0.50m, 0),
            new Faixa("acima de 50%", _ => true, -15),
        }),
        new Caracteristica("relacionamento", new[]
        {
            new Faixa("cliente atual", p => p.ClienteAtual, 10),
            new Faixa("novo", _ => true, 0),
        }),
    };

    /// <summary>Parcela das dívidas existentes mais despesas sobre a renda.</summary>
    public static decimal Comprometimento(Proposta p) => p.RendaMensal <= 0 ? 1m : (p.DespesasMensais + p.DividasMensaisExistentes) / p.RendaMensal;
}
