using Credito.Core.Financeiro;

namespace Credito.Core.Analise;

public enum Decisao
{
    Aprovado,
    AprovadoComAjuste,
    AnaliseManual,
    Recusado,
}

/// <summary>Parâmetros da política de crédito.</summary>
public sealed record ParametrosPolitica(
    int IdadeMinima = 18,
    int IdadeMaxima = 75,
    int ScoreBureauMinimo = 300,
    decimal ComprometimentoMaximo = 0.35m,
    decimal ValorMinimo = 500m,
    decimal ValorMaximo = 200_000m,
    int PrazoMinimo = 3,
    int PrazoMaximo = 72,
    decimal MultiploDeRendaMaximo = 15m,
    Sistema SistemaPadrao = Sistema.Price,
    decimal TarifaCadastro = 0m);

public sealed record Oferta(decimal Valor, int PrazoMeses, decimal TaxaMensal, decimal Parcela, decimal ComprometimentoResultante, ResultadoCet Cet, IReadOnlyList<Parcela> Plano);

public sealed record Avaliacao(
    string PropostaId,
    Decisao Decisao,
    int Pontuacao,
    FaixaDeRisco FaixaDeRisco,
    IReadOnlyList<PontoDoScore> Detalhe,
    IReadOnlyList<string> Motivos,
    Oferta? Oferta,
    decimal LimiteMaximo);

/// <summary>
/// Política: cortes duros (recusa), scorecard (faixa e taxa), capacidade de pagamento (comprometimento máximo),
/// e contraproposta quando o pedido não cabe: reduz o valor ao que cabe na renda, mantendo o prazo.
/// </summary>
public sealed class PoliticaDeCredito
{
    private readonly Scorecard _scorecard;

    public PoliticaDeCredito(ParametrosPolitica? parametros = null, Scorecard? scorecard = null)
    {
        Parametros = parametros ?? new ParametrosPolitica();
        _scorecard = scorecard ?? new Scorecard();
    }

    public ParametrosPolitica Parametros { get; }

    public Avaliacao Avaliar(Proposta p, DateOnly hoje)
    {
        var motivos = new List<string>();
        var (pontos, detalhe) = _scorecard.Pontuar(p);
        var faixa = Scorecard.Classificar(pontos);
        var taxa = Scorecard.TaxaMensal(faixa);

        // cortes duros
        if (p.RestricaoAtiva) motivos.Add("restrição ativa em bureau");
        if (p.Idade < Parametros.IdadeMinima || p.Idade > Parametros.IdadeMaxima) motivos.Add($"idade fora da política ({Parametros.IdadeMinima} a {Parametros.IdadeMaxima})");
        if (p.ScoreBureau < Parametros.ScoreBureauMinimo) motivos.Add($"score do bureau abaixo de {Parametros.ScoreBureauMinimo}");
        if (p.RendaMensal <= 0) motivos.Add("renda não informada");
        if (p.ValorSolicitado < Parametros.ValorMinimo || p.ValorSolicitado > Parametros.ValorMaximo) motivos.Add($"valor fora da política (R$ {Parametros.ValorMinimo:N0} a R$ {Parametros.ValorMaximo:N0})");
        if (p.PrazoMeses < Parametros.PrazoMinimo || p.PrazoMeses > Parametros.PrazoMaximo) motivos.Add($"prazo fora da política ({Parametros.PrazoMinimo} a {Parametros.PrazoMaximo} meses)");
        if (faixa == FaixaDeRisco.E) motivos.Add("pontuação insuficiente (faixa E)");
        if (motivos.Count > 0) return new Avaliacao(p.Id, Decisao.Recusado, pontos, faixa, detalhe, motivos, null, 0m);

        // capacidade: parcela máxima que cabe no comprometimento
        var parcelaMaxima = Amortizacao.Arredondar(p.RendaMensal * Parametros.ComprometimentoMaximo - p.DespesasMensais - p.DividasMensaisExistentes);
        var limitePorRenda = Amortizacao.Arredondar(p.RendaMensal * Parametros.MultiploDeRendaMaximo);
        var limitePorParcela = parcelaMaxima <= 0 ? 0m : PrincipalPara(parcelaMaxima, taxa, p.PrazoMeses);
        var limite = Math.Min(Parametros.ValorMaximo, Math.Min(limitePorRenda, limitePorParcela));
        if (limite < Parametros.ValorMinimo)
        {
            motivos.Add("renda livre insuficiente para qualquer parcela dentro do comprometimento máximo");
            return new Avaliacao(p.Id, Decisao.Recusado, pontos, faixa, detalhe, motivos, null, 0m);
        }

        var valor = Math.Min(p.ValorSolicitado, limite);
        var ajustado = valor < p.ValorSolicitado;
        var oferta = MontarOferta(p, valor, taxa, hoje);
        if (ajustado) motivos.Add($"valor reduzido de R$ {p.ValorSolicitado:N2} para R$ {valor:N2} para caber em {Parametros.ComprometimentoMaximo:P0} da renda");

        var decisao = faixa == FaixaDeRisco.D ? Decisao.AnaliseManual : ajustado ? Decisao.AprovadoComAjuste : Decisao.Aprovado;
        if (faixa == FaixaDeRisco.D) motivos.Add("faixa D exige análise manual");
        return new Avaliacao(p.Id, decisao, pontos, faixa, detalhe, motivos, oferta, limite);
    }

    /// <summary>Simula sem política: só o plano e o CET para um valor, taxa e prazo dados.</summary>
    public Oferta Simular(decimal valor, decimal taxaMensal, int prazo, Sistema sistema, DateOnly hoje, decimal rendaMensal = 0m, decimal outrasParcelas = 0m)
    {
        var liberacao = hoje;
        var plano = Amortizacao.Plano(sistema, valor, taxaMensal, prazo, hoje.AddMonths(1));
        var cet = Cet.Calcular(plano, taxaMensal, liberacao, new Encargos(Parametros.TarifaCadastro));
        var parcela = plano[0].Valor;
        var comprometimento = rendaMensal <= 0 ? 0m : decimal.Round((parcela + outrasParcelas) / rendaMensal, 4);
        return new Oferta(valor, prazo, taxaMensal, parcela, comprometimento, cet, plano);
    }

    private Oferta MontarOferta(Proposta p, decimal valor, decimal taxa, DateOnly hoje) =>
        Simular(valor, taxa, p.PrazoMeses, Parametros.SistemaPadrao, hoje, p.RendaMensal, p.DespesasMensais + p.DividasMensaisExistentes);

    /// <summary>Principal cuja parcela Price é igual à parcela máxima: PV = PMT·(1 − (1+i)^−n)/i.</summary>
    public static decimal PrincipalPara(decimal parcela, decimal taxaMensal, int prazo)
    {
        if (taxaMensal == 0) return Amortizacao.Arredondar(parcela * prazo);
        var i = (double)taxaMensal;
        return Amortizacao.Arredondar((decimal)((double)parcela * (1 - Math.Pow(1 + i, -prazo)) / i));
    }
}
